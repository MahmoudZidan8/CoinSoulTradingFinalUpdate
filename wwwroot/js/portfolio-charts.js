// Portfolio Intelligence Center - Chart Rendering
// Blazor Server Safe - No prerender issues

(function() {
    'use strict';

    /**
     * Renders allocation pie chart showing top holdings distribution
     * @param {string[]} labels - Asset names
     * @param {number[]} values - USDT values
     */
    window.renderAllocationChart = function (labels, values) {
        const ctx = document.getElementById('allocationChart');
        if (!ctx) {
            console.warn('allocationChart canvas not found');
            return;
        }

        // Destroy existing instance to prevent memory leaks
        if (window.allocationChartInstance) {
            window.allocationChartInstance.destroy();
            window.allocationChartInstance = null;
        }

        // Validate Chart.js is loaded
        if (typeof Chart === 'undefined') {
            console.error('Chart.js library not loaded');
            return;
        }

        const colors = [
            '#3b82f6', // blue
            '#22c55e', // green
            '#f59e0b', // amber
            '#ef4444', // red
            '#8b5cf6', // purple
            '#94a3b8'  // slate (others)
        ];

        try {
            window.allocationChartInstance = new Chart(ctx, {
                type: 'pie',
                data: {
                    labels: labels,
                    datasets: [{
                        data: values,
                        backgroundColor: colors.slice(0, values.length),
                        borderColor: 'rgba(15,23,42,1)',
                        borderWidth: 2
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: {
                            position: 'bottom',
                            labels: { 
                                color: '#e2e8f0',
                                font: { 
                                    size: 11,
                                    family: "'Inter', sans-serif"
                                },
                                padding: 10
                            }
                        },
                        tooltip: {
                            backgroundColor: 'rgba(15,23,42,0.95)',
                            titleColor: '#f1f5f9',
                            bodyColor: '#e2e8f0',
                            borderColor: 'rgba(148,163,184,0.3)',
                            borderWidth: 1,
                            padding: 12,
                            displayColors: true,
                            callbacks: {
                                label: function(context) {
                                    const label = context.label || '';
                                    const value = context.parsed || 0;
                                    const total = context.dataset.data.reduce((a, b) => a + b, 0);
                                    const percentage = total > 0 ? ((value / total) * 100).toFixed(2) : '0.00';
                                    return `${label}: $${value.toFixed(2)} (${percentage}%)`;
                                }
                            }
                        }
                    }
                }
            });
        } catch (error) {
            console.error('Failed to create allocation chart:', error);
        }
    };

    /**
     * Renders equity trend line chart for 24h period
     * @param {string[]} labels - Time labels
     * @param {number[]} values - Equity values in USDT
     */
    window.renderEquityTrendChart = function (labels, values) {
        const ctx = document.getElementById('equityTrendChart');
        if (!ctx) {
            console.warn('equityTrendChart canvas not found');
            return;
        }

        // Destroy existing instance to prevent memory leaks
        if (window.equityTrendChartInstance) {
            window.equityTrendChartInstance.destroy();
            window.equityTrendChartInstance = null;
        }

        // Validate Chart.js is loaded
        if (typeof Chart === 'undefined') {
            console.error('Chart.js library not loaded');
            return;
        }

        try {
            window.equityTrendChartInstance = new Chart(ctx, {
                type: 'line',
                data: {
                    labels: labels,
                    datasets: [{
                        label: 'Equity (USDT)',
                        data: values,
                        borderColor: '#3b82f6',
                        backgroundColor: 'rgba(59,130,246,0.1)',
                        tension: 0.4,
                        fill: true,
                        pointBackgroundColor: '#3b82f6',
                        pointBorderColor: '#fff',
                        pointBorderWidth: 2,
                        pointRadius: values.length > 20 ? 2 : 4,
                        pointHoverRadius: 6
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    interaction: {
                        mode: 'index',
                        intersect: false
                    },
                    plugins: {
                        legend: { 
                            display: false
                        },
                        tooltip: {
                            backgroundColor: 'rgba(15,23,42,0.95)',
                            titleColor: '#f1f5f9',
                            bodyColor: '#e2e8f0',
                            borderColor: 'rgba(148,163,184,0.3)',
                            borderWidth: 1,
                            padding: 12,
                            displayColors: false,
                            callbacks: {
                                label: function(context) {
                                    return `Equity: $${context.parsed.y.toFixed(2)}`;
                                }
                            }
                        }
                    },
                    scales: {
                        x: {
                            ticks: { 
                                color: '#94a3b8',
                                font: { 
                                    size: 10,
                                    family: "'Inter', sans-serif"
                                },
                                maxRotation: 45,
                                minRotation: 0
                            },
                            grid: { 
                                color: 'rgba(148,163,184,0.1)',
                                drawBorder: false
                            }
                        },
                        y: {
                            ticks: { 
                                color: '#94a3b8',
                                font: { 
                                    size: 10,
                                    family: "'Inter', sans-serif"
                                },
                                callback: function(value) {
                                    return '$' + value.toFixed(2);
                                }
                            },
                            grid: { 
                                color: 'rgba(148,163,184,0.1)',
                                drawBorder: false
                            }
                        }
                    }
                }
            });
        } catch (error) {
            console.error('Failed to create equity trend chart:', error);
        }
    };

    /**
     * Cleanup function to destroy all chart instances
     * Call this when navigating away from portfolio page
     */
    window.destroyPortfolioCharts = function() {
        if (window.allocationChartInstance) {
            window.allocationChartInstance.destroy();
            window.allocationChartInstance = null;
        }
        if (window.equityTrendChartInstance) {
            window.equityTrendChartInstance.destroy();
            window.equityTrendChartInstance = null;
        }
    };

})();