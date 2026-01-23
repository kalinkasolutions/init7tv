export async function loadPartial(url) {
    const response = await fetch(url);
    return await response.text();
}