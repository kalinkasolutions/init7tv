export async function loadPartial(url) {
    const response = await fetch(url);

    return response.text();
}
