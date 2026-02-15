# Init7TV

Init7TV is a self-hosted IPTV web application for Init7 fiber subscribers. It provides a clean browser-based interface for watching live TV channels over your Init7 internet connection, including an integrated EPG (Electronic Programme Guide), stream management, and user authentication.

---

## Requirements

- Docker and Docker Compose
- An Init7 fiber subscription with access to the Init7 TV service
- Either an nginx reverse proxy with SSL termination, or a certificate for direct Kestrel HTTPS

---

## Setup

### Option A: Behind a reverse proxy (nginx)

#### 1. Create the docker-compose.yml

```yaml
services:
  app:
    container_name: init7tv
    image: kalinkasolutions/init7tv:latest
    ports:
      - "7880:5001"
    restart: always
    volumes:
      - ./data:/var/srv
    environment:
      - ProxyAddress=10.10.0.1   # IP address of your nginx proxy
      - KESTREL__ENDPOINTS__HTTPS__URL=http://*:5001
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

    # WebSocket support (required for live dashboard)
    proxy_http_version  1.1;
    proxy_set_header    Upgrade     $http_upgrade;
    proxy_set_header    Connection  "upgrade";
    proxy_redirect      off;
    proxy_buffering     off;

    location / {
        proxy_pass http://127.0.0.1:7880;
    }
}
```

> **Important:** The `X-Forwarded-Proto` header is required. Without it the application will generate `http://` redirect URLs, which browsers will block as mixed content.

---

### Option B: Direct HTTPS (no reverse proxy)

#### 1. Create the docker-compose.yml

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

Navigate to `https://tv.example.com` and log in with the default admin credentials. It is strongly recommended to change the password immediately after first login.

---

## Configuration

| Environment Variable | Description | Required |
|---|---|---|
| `ProxyAddress` | IP address of your nginx reverse proxy | Yes (Option A only) |
| `KESTREL__CERTIFICATES__DEFAULT__PATH` | Path to the `.pfx` certificate inside the container | Yes (Option B only) |
| `KESTREL__CERTIFICATES__DEFAULT__PASSWORD` | Password for the `.pfx` certificate | Yes (Option B only) |

---

## Updating

```bash
docker compose pull
docker compose up -d
```

Database migrations are applied automatically on startup.
