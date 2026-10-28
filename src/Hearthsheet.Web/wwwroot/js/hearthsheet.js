// Hand-written browser glue; later tasks add storage and tab-lock exports.
if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' });
}
