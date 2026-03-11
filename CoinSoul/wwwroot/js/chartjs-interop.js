// Minimal Chart.js interop for Blazor (no leaks, stable keys)
window.chartjsInterop = (function () {
    const _charts = {};

    function destroy(key) {
        const chart = _charts[key];
        if (chart) {
            try { chart.destroy(); } catch (e) { }
            delete _charts[key];
        }
    }

    function render(key, canvas, config) {
        if (!key) throw new Error("chart key is required");

        let el = canvas;
        if (typeof canvas === "string") el = document.getElementById(canvas);
        if (!el) throw new Error("canvas not found");

        destroy(key);

        const ctx = el.getContext("2d");
        if (!ctx) throw new Error("cannot get 2d context");

        _charts[key] = new Chart(ctx, config);
    }

    function update(key, labels, datasets) {
        const chart = _charts[key];
        if (!chart) return;

        chart.data.labels = labels;
        chart.data.datasets = datasets;
        chart.update();
    }

    return { render, update, destroy };
})();

// Backward-compatible wrapper used by Razor components in this project.
// .NET calls: coinSoulCharts.render(key, type, labels, datasets, options)
// Canvas id convention: chart-{key}
window.coinSoulCharts = (function () {
    function _toCanvasId(key) {
        return `chart-${key}`;
    }

    function destroy(key) {
        try { window.chartjsInterop.destroy(key); } catch (e) { }
    }

    function render(key, type, labels, datasets, options) {
        const canvasId = _toCanvasId(key);

        const baseOptions = {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: { display: true },
                tooltip: { enabled: true }
            },
            scales: {
                x: { ticks: { color: '#9ca3af' }, grid: { color: 'rgba(255,255,255,0.06)' } },
                y: { ticks: { color: '#9ca3af' }, grid: { color: 'rgba(255,255,255,0.06)' } }
            }
        };

        const config = {
            type: type,
            data: { labels: labels, datasets: datasets },
            options: Object.assign(baseOptions, (options || {}))
        };

        // Defensive: Blazor cannot marshal JS function callbacks in options.
        // If options includes functions (e.g., tick callbacks), they will be dropped.
        try {
            config.options = JSON.parse(JSON.stringify(config.options));
        } catch (e) { }

        window.chartjsInterop.render(key, canvasId, config);
    }

    return { render, destroy };
})();

window.renderEquityChart = function (canvasId, labels, values) {

    const ctx = document.getElementById(canvasId);

    if (!ctx) return;

    new Chart(ctx, {
        type: 'line',
        data: {
            labels: labels,
            datasets: [{
                label: 'Equity',
                data: values,
                borderColor: '#22c55e',
                backgroundColor: 'rgba(34,197,94,0.1)',
                tension: 0.3
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false
        }
    });
};
