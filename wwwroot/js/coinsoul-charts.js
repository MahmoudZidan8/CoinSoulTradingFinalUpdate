// CoinSoul Charts Module
window.coinSoulCharts = window.coinSoulCharts || {};
window.coinSoulCharts._chartDaily = null;

window.coinSoulCharts.renderDailyPnl = function(labels, values) {
    const ctx = document.getElementById('dailyPnlChart');
    if (!ctx) {
        console.warn('[CoinSoulCharts] Canvas element "dailyPnlChart" not found');
        return;
    }

    // Destroy existing chart
    if (window.coinSoulCharts._chartDaily) {
        window.coinSoulCharts._chartDaily.destroy();
        window.coinSoulCharts._chartDaily = null;
    }

    // Create new chart
    window.coinSoulCharts._chartDaily = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: labels,
            datasets: [{
                label: 'Net PnL (USDT)',
                data: values,
                backgroundColor: values.map(v => v >= 0 ? 'rgba(76, 175, 80, 0.7)' : 'rgba(244, 67, 54, 0.7)'),
                borderColor: values.map(v => v >= 0 ? 'rgba(76, 175, 80, 1)' : 'rgba(244, 67, 54, 1)'),
                borderWidth: 1
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: { 
                legend: { display: true },
                tooltip: {
                    callbacks: {
                        label: function(context) {
                            let label = context.dataset.label || '';
                            if (label) label += ': ';
                            if (context.parsed.y !== null) {
                                label += context.parsed.y.toFixed(2) + ' USDT';
                            }
                            return label;
                        }
                    }
                }
            },
            scales: {
                y: { 
                    beginAtZero: true,
                    ticks: {
                        callback: function(value) {
                            return value.toFixed(2) + ' USDT';
                        }
                    }
                }
            }
        }
    });

    console.log('[CoinSoulCharts] Daily PnL chart rendered');
};// CoinSoul Charts Module
window.coinSoulCharts = window.coinSoulCharts || {};
window.coinSoulCharts._chartDaily = null;

window.coinSoulCharts.renderDailyPnl = function(labels, values) {
    const ctx = document.getElementById('dailyPnlChart');
    if (!ctx) {
        console.warn('[CoinSoulCharts] Canvas element "dailyPnlChart" not found');
        return;
    }

    // Destroy existing chart
    if (window.coinSoulCharts._chartDaily) {
        window.coinSoulCharts._chartDaily.destroy();
        window.coinSoulCharts._chartDaily = null;
    }

    // Create new chart
    window.coinSoulCharts._chartDaily = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: labels,
            datasets: [{
                label: 'Net PnL (USDT)',
                data: values,
                backgroundColor: values.map(v => v >= 0 ? 'rgba(76, 175, 80, 0.7)' : 'rgba(244, 67, 54, 0.7)'),
                borderColor: values.map(v => v >= 0 ? 'rgba(76, 175, 80, 1)' : 'rgba(244, 67, 54, 1)'),
                borderWidth: 1
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: { 
                legend: { display: true },
                tooltip: {
                    callbacks: {
                        label: function(context) {
                            let label = context.dataset.label || '';
                            if (label) label += ': ';
                            if (context.parsed.y !== null) {
                                label += context.parsed.y.toFixed(2) + ' USDT';
                            }
                            return label;
                        }
                    }
                }
            },
            scales: {
                y: { 
                    beginAtZero: true,
                    ticks: {
                        callback: function(value) {
                            return value.toFixed(2) + ' USDT';
                        }
                    }
                }
            }
        }
    });

    console.log('[CoinSoulCharts] Daily PnL chart rendered');
};// CoinSoul Charts Module
window.coinSoulCharts = window.coinSoulCharts || {};
window.coinSoulCharts._chartDaily = null;

window.coinSoulCharts.renderDailyPnl = function(labels, values) {
    const ctx = document.getElementById('dailyPnlChart');
    if (!ctx) {
        console.warn('[CoinSoulCharts] Canvas element "dailyPnlChart" not found');
        return;
    }

    // Destroy existing chart
    if (window.coinSoulCharts._chartDaily) {
        window.coinSoulCharts._chartDaily.destroy();
        window.coinSoulCharts._chartDaily = null;
    }

    // Create new chart
    window.coinSoulCharts._chartDaily = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: labels,
            datasets: [{
                label: 'Net PnL (USDT)',
                data: values,
                backgroundColor: values.map(v => v >= 0 ? 'rgba(76, 175, 80, 0.7)' : 'rgba(244, 67, 54, 0.7)'),
                borderColor: values.map(v => v >= 0 ? 'rgba(76, 175, 80, 1)' : 'rgba(244, 67, 54, 1)'),
                borderWidth: 1
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: { 
                legend: { display: true },
                tooltip: {
                    callbacks: {
                        label: function(context) {
                            let label = context.dataset.label || '';
                            if (label) label += ': ';
                            if (context.parsed.y !== null) {
                                label += context.parsed.y.toFixed(2) + ' USDT';
                            }
                            return label;
                        }
                    }
                }
            },
            scales: {
                y: { 
                    beginAtZero: true,
                    ticks: {
                        callback: function(value) {
                            return value.toFixed(2) + ' USDT';
                        }
                    }
                }
            }
        }
    });

    console.log('[CoinSoulCharts] Daily PnL chart rendered');
};