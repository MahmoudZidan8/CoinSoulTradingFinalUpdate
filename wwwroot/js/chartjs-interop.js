window.renderEquityChart = function (labels, equity, drawdown) {
    const ctx = document.getElementById('equityChart');
    if (!ctx) return;

    if (window.equityChartInstance)
        window.equityChartInstance.destroy();

    window.equityChartInstance = new Chart(ctx, {
        type: 'line',
        data: {
            labels: labels,
            datasets: [
                {
                    label: 'Equity',
                    data: equity,
                    borderColor: '#22c55e',
                    backgroundColor: 'rgba(34,197,94,.1)',
                    tension: 0.3,
                    fill: true,
                    yAxisID: 'y'
                },
                {
                    label: 'Drawdown',
                    data: drawdown,
                    borderColor: '#ef4444',
                    backgroundColor: 'rgba(239,68,68,.1)',
                    tension: 0.3,
                    fill: true,
                    yAxisID: 'y1'
                }
            ]
        },
        options: {
            responsive: true,
            plugins: {
                legend: { labels: { color: '#e2e8f0' } },
                tooltip: {
                    backgroundColor: 'rgba(15,23,42,0.9)',
                    titleColor: '#f1f5f9',
                    bodyColor: '#e2e8f0'
                }
            },
            scales: {
                x: {
                    ticks: { color: '#94a3b8' },
                    grid: { color: 'rgba(148,163,184,.1)' }
                },
                y: {
                    type: 'linear',
                    display: true,
                    position: 'left',
                    ticks: { color: '#22c55e' },
                    grid: { color: 'rgba(148,163,184,.1)' }
                },
                y1: {
                    type: 'linear',
                    display: true,
                    position: 'right',
                    ticks: { color: '#ef4444' },
                    grid: { drawOnChartArea: false }
                }
            }
        }
    });
};

window.renderDailyPnlChart = function (labels, pnl) {
    const ctx = document.getElementById('dailyPnlChart');
    if (!ctx) return;

    if (window.dailyPnlChartInstance)
        window.dailyPnlChartInstance.destroy();

    const colors = pnl.map(v => v >= 0 ? '#22c55e' : '#ef4444');

    window.dailyPnlChartInstance = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: labels,
            datasets: [{
                label: 'Daily P&L',
                data: pnl,
                backgroundColor: colors,
                borderColor: colors,
                borderWidth: 1
            }]
        },
        options: {
            responsive: true,
            plugins: {
                legend: { display: false },
                tooltip: {
                    backgroundColor: 'rgba(15,23,42,0.9)',
                    titleColor: '#f1f5f9',
                    bodyColor: '#e2e8f0'
                }
            },
            scales: {
                x: {
                    ticks: { color: '#94a3b8' },
                    grid: { color: 'rgba(148,163,184,.1)' }
                },
                y: {
                    ticks: { color: '#94a3b8' },
                    grid: { color: 'rgba(148,163,184,.1)' }
                }
            }
        }
    });
};

window.renderMonthlyChart = function (labels, pnl) {
    const ctx = document.getElementById('monthlyChart');
    if (!ctx) return;

    if (window.monthlyChartInstance)
        window.monthlyChartInstance.destroy();

    const colors = pnl.map(v => v >= 0 ? '#22c55e' : '#ef4444');

    window.monthlyChartInstance = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: labels,
            datasets: [{
                label: 'Monthly P&L',
                data: pnl,
                backgroundColor: colors,
                borderColor: colors,
                borderWidth: 1
            }]
        },
        options: {
            responsive: true,
            plugins: {
                legend: { display: false },
                tooltip: {
                    backgroundColor: 'rgba(15,23,42,0.9)',
                    titleColor: '#f1f5f9',
                    bodyColor: '#e2e8f0'
                }
            },
            scales: {
                x: {
                    ticks: { color: '#94a3b8' },
                    grid: { color: 'rgba(148,163,184,.1)' }
                },
                y: {
                    ticks: { color: '#94a3b8' },
                    grid: { color: 'rgba(148,163,184,.1)' }
                }
            }
        }
    });
};

window.downloadCsv = function (filename, content) {
    const blob = new Blob([content], { type: 'text/csv' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    window.URL.revokeObjectURL(url);
    document.body.removeChild(a);
};