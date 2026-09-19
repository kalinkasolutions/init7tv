# Init7TV

Init7TV is a self-hosted IPTV web application for Init7 fiber subscribers. It provides a clean browser-based interface
for watching live TV channels over your Init7 internet connection, including an integrated EPG (Electronic Programme
Guide), stream management, and user authentication.

---

## Requirements

- Docker and Docker Compose
- An Init7 fiber subscription with access to the Init7 TV service
- Either an nginx reverse proxy with SSL termination, or a certificate for direct Kestrel HTTPS

---

## Multicast vs. HLS

Init7TV can receive IPTV streams in two modes:

- **Multicast** (`UseMultiCast=true`, default): The application joins IP multicast groups to receive streams directly
  from the Init7 network. This is more efficient as the stream is delivered once to the host regardless of how many
  viewers are watching. **Requires `network_mode: host`** in Docker so the container can join multicast groups on the
  host's network interface.

- **HLS** (`UseMultiCast=false`): The application fetches streams over HTTP instead. Standard Docker bridge networking
  with `ports` mappings works fine in this mode.

---

## Setup

### Option A: Behind a reverse proxy (nginx)

#### 1. Create the docker-compose.yml

**With multicast (`network_mode: host`):**

```yaml
services:
  app:
    container_name: init7tv
    image: kalinkasolutions/init7tv:latest
    network_mode: host
    restart: always
    volumes:
      - ./data:/var/srv
    environment:
      - ProxyAddress=10.10.0.1   # IP address of your nginx proxy
      - Init7TvOptions__UseMultiCast=true
      - ASPNETCORE_HTTP_PORTS=8080
```

**With HLS (bridge networking):**

```yaml
services:
  app:
    container_name: init7tv
    image: kalinkasolutions/init7tv:latest
    ports:
      - "7880:8080"
    restart: always
    volumes:
      - ./data:/var/srv
    environment:
      - ProxyAddress=10.10.0.1   # IP address of your nginx proxy
      - Init7TvOptions__UseMultiCast=false
```

The `data` directory will be created automatically and contains the SQLite database.

#### 2. Configure nginx

```nginx
server {
    listen 80;
    listen [::]:80;
    server_name tv.example.com;
    return 301 https://$server_name$request_uri;
}

server {
    listen 443 ssl http2;
    listen [::]:443 http2 ssl;
    server_name tv.example.com;

    ssl_certificate     /path/to/cert.pem;
    ssl_certificate_key /path/to/key.pem;

    proxy_set_header Host                $host;
    proxy_set_header X-Real-IP           $remote_addr;
    proxy_set_header X-Forwarded-For     $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto   $scheme;
    proxy_set_header X-Forwarded-Host    $host;

    # WebSocket support (live dashboard) and unbuffered responses, which the
    # player's server-sent event stream needs to receive updates promptly
    proxy_http_version  1.1;
    proxy_set_header    Upgrade     $http_upgrade;
    proxy_set_header    Connection  "upgrade";
    proxy_redirect      off;
    proxy_buffering     off;

    location / {
        # Use port 8080 for multicast (host networking), 7880 for HLS (bridge networking)
        proxy_pass http://127.0.0.1:8080;
    }
}
```

> **Important:** The `X-Forwarded-Proto` header is required. Without it the application will generate `http://` redirect
> URLs, which browsers will block as mixed content.

---

### Option B: Direct HTTPS (no reverse proxy)

#### 1. Create the docker-compose.yml

**With multicast (`network_mode: host`):**

```yaml
services:
  app:
    container_name: init7tv
    image: kalinkasolutions/init7tv:latest
    network_mode: host
    restart: always
    volumes:
      - ./data:/var/srv
      - ./certs:/var/certs
    environment:
      - KESTREL__CERTIFICATES__DEFAULT__PATH=/var/certs/cert.pfx
      - KESTREL__CERTIFICATES__DEFAULT__PASSWORD=yourpassword
      - Init7TvOptions__UseMultiCast=true
      - ASPNETCORE_HTTPS_PORTS=5001
```

**With HLS (bridge networking):**

```yaml
services:
  app:
    container_name: init7tv
    image: kalinkasolutions/init7tv:latest
    ports:
      - "5001:5001"
    restart: always
    volumes:
      - ./data:/var/srv
      - ./certs:/var/certs
    environment:
      - KESTREL__CERTIFICATES__DEFAULT__PATH=/var/certs/cert.pfx
      - KESTREL__CERTIFICATES__DEFAULT__PASSWORD=yourpassword
      - Init7TvOptions__UseMultiCast=false
      - ASPNETCORE_HTTPS_PORTS=5001
```

Place your `.pfx` certificate in the `./certs` directory. No `ProxyAddress` is needed since there is no proxy.

---

### Start the application

```bash
docker compose up -d
```

On first start the database is created and seeded with a default admin account:

```
user: admin
password: admin
```

### Log in

Navigate to `https://tv.example.com` and log in with the default admin credentials. It is strongly recommended to change
the password immediately after first login.

---

## Configuration

| Environment Variable                       | Description                                                                                    | Default   | Required            |
|--------------------------------------------|------------------------------------------------------------------------------------------------|-----------|---------------------|
| `ProxyAddress`                             | IP address of your nginx reverse proxy                                                         | —         | Yes (Option A only) |
| `KESTREL__CERTIFICATES__DEFAULT__PATH`     | Path to the `.pfx` certificate inside the container                                            | —         | Yes (Option B only) |
| `KESTREL__CERTIFICATES__DEFAULT__PASSWORD` | Password for the `.pfx` certificate                                                            | —         | Yes (Option B only) |
| `Init7TvOptions__UseMultiCast`             | Enable multicast stream reception. Requires `network_mode: host`. Set to `false` to use HLS.  | `true`    | No                  |
| `Init7TvOptions__RecordingPath`            | Where recordings are written. Keep it inside the data volume so they survive an update.        | `/var/srv/recordings` | No      |
| `Init7TvOptions__MaxConcurrentRecordings`  | How many programmes may record at once. Each one is a separate FFmpeg encode.                  | `2`       | No                  |

The FFmpeg preset and log level are not environment variables; they are configured at runtime under **admin → General
Settings**, along with the recording preset and how many minutes to start early and keep going after.

---

## Recording

Users with the **Recording** role, and admins, get a recording page: pick a programme from the guide and the
application records it when the time comes, whether or not anybody is watching.

Recordings are written to `/var/srv/recordings`, one directory per recording, inside the same `./data` volume as the
database. They are captured as a transport stream and wrapped as MP4 when they finish, so a crash costs the tail of a
recording rather than all of it, and the finished file plays and seeks in the browser.

A few things worth knowing:

- **Each recording is an encode.** With the default `veryfast` preset expect roughly 2.5 GB per hour on an HD channel
  and about a core per recording, on top of whatever live viewers are using. `Init7TvOptions__MaxConcurrentRecordings` is the ceiling;
  anything over it is skipped with a reason shown on the page.
- **The database shares the volume.** The recorder refuses to start with less than 5 GB free, because filling the disk
  would take the database with it.
- **Two people picking the same programme get one recording**, one encode and one file, listed for each of them. The
  file goes when the last of them deletes it.
- **A restart is survivable.** Anything that was recording is picked up again on the next start, and either continues
  or is wrapped up as a partial recording.

---

## Updating

```bash
docker compose pull
docker compose up -d
```

Database migrations are applied automatically on startup.
