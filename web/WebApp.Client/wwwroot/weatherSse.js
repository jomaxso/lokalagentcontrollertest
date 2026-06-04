export function connectWeatherStream(url, dotNetReference) {
    const eventSource = new EventSource(url);

    const handle = {
        close() {
            eventSource.close();
        }
    };

    eventSource.onopen = async () => {
        await dotNetReference.invokeMethodAsync("HandleStreamOpened");
    };

    eventSource.addEventListener("forecast", async event => {
        await dotNetReference.invokeMethodAsync("ReceiveForecasts", event.data);
    });

    eventSource.onerror = async () => {
        await dotNetReference.invokeMethodAsync("HandleStreamError", `Keine Verbindung zu ${url}`);
    };

    return handle;
}