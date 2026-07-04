import '/_content/CloudShell.Hosting/d3.v7.min.js';

const observers = new WeakMap();

export function renderRabbitMQQueueDepth(element, queues) {
    if (!element) {
        return;
    }

    const d3 = globalThis.d3;
    if (!d3) {
        element.replaceChildren();
        element.textContent = 'D3 is unavailable.';
        return;
    }

    const data = (queues || []).map(queue => ({
        name: String(queue.name || ''),
        ready: Math.max(Number(queue.ready || 0), 0),
        unacknowledged: Math.max(Number(queue.unacknowledged || 0), 0),
        consumers: Math.max(Number(queue.consumers || 0), 0)
    }));

    drawQueueDepth(element, data, d3);
    ensureObserver(element, () => drawQueueDepth(element, data, d3));
}

export function renderRabbitMQThroughput(element, samples) {
    if (!element) {
        return;
    }

    const d3 = globalThis.d3;
    if (!d3) {
        element.replaceChildren();
        element.textContent = 'D3 is unavailable.';
        return;
    }

    const data = (samples || [])
        .map(sample => ({
            timestamp: Number(sample.timestamp || 0),
            publish: nullableNumber(sample.publish),
            deliverGet: nullableNumber(sample.deliverGet),
            ack: nullableNumber(sample.ack)
        }))
        .filter(sample => sample.timestamp > 0)
        .sort((left, right) => left.timestamp - right.timestamp);

    drawThroughput(element, data, d3);
    ensureObserver(element, () => drawThroughput(element, data, d3));
}

export function disposeRabbitMQChart(element) {
    const observer = observers.get(element);
    if (observer) {
        observer.disconnect();
        observers.delete(element);
    }
}

function ensureObserver(element, redraw) {
    let observer = observers.get(element);
    if (observer) {
        observer.disconnect();
    }

    observer = new ResizeObserver(redraw);
    observer.observe(element);
    observers.set(element, observer);
}

function drawThroughput(element, data, d3) {
    element.replaceChildren();

    const width = Math.max(element.clientWidth || 0, 420);
    const margin = { top: 30, right: 26, bottom: 42, left: 58 };
    const height = 280;
    const series = [
        { key: 'publish', label: 'Publish', className: 'throughput-publish' },
        { key: 'deliverGet', label: 'Deliver/Get', className: 'throughput-deliver' },
        { key: 'ack', label: 'Ack', className: 'throughput-ack' }
    ];
    const values = data.flatMap(sample => series
        .map(item => sample[item.key])
        .filter(value => value !== null));
    const maxRate = Math.max(1, d3.max(values) || 0);
    const extent = d3.extent(data, sample => sample.timestamp);
    const minTime = extent[0] ?? Date.now() - 300000;
    const maxTime = extent[1] && extent[1] > minTime ? extent[1] : minTime + 300000;

    const svg = d3.select(element)
        .append('svg')
        .attr('viewBox', `0 0 ${width} ${height}`)
        .attr('role', 'img')
        .attr('aria-label', 'RabbitMQ throughput');

    const x = d3.scaleTime()
        .domain([new Date(minTime), new Date(maxTime)])
        .range([margin.left, width - margin.right]);

    const y = d3.scaleLinear()
        .domain([0, maxRate])
        .nice()
        .range([height - margin.bottom, margin.top]);

    svg.append('g')
        .attr('class', 'axis')
        .attr('transform', `translate(0,${height - margin.bottom})`)
        .call(d3.axisBottom(x).ticks(Math.min(5, Math.max(2, width / 180))).tickFormat(d3.timeFormat('%H:%M:%S')));

    svg.append('g')
        .attr('class', 'axis')
        .attr('transform', `translate(${margin.left},0)`)
        .call(d3.axisLeft(y).ticks(5).tickFormat(value => `${d3.format('~s')(value)}/s`));

    const line = key => d3.line()
        .defined(sample => sample[key] !== null)
        .x(sample => x(new Date(sample.timestamp)))
        .y(sample => y(sample[key] || 0));

    for (const item of series) {
        svg.append('path')
            .datum(data)
            .attr('class', `throughput-line ${item.className}`)
            .attr('fill', 'none')
            .attr('stroke-width', 2.4)
            .attr('d', line(item.key));

        svg.append('g')
            .selectAll('circle')
            .data(data.filter(sample => sample[item.key] !== null))
            .join('circle')
            .attr('class', `throughput-point ${item.className}`)
            .attr('cx', sample => x(new Date(sample.timestamp)))
            .attr('cy', sample => y(sample[item.key] || 0))
            .attr('r', 2.7);
    }

    const legend = svg.append('g')
        .attr('transform', `translate(${margin.left},12)`);

    series.forEach((item, index) => appendLineLegendItem(legend, index * 104, item.className, item.label));
}

function drawQueueDepth(element, data, d3) {
    element.replaceChildren();

    const width = Math.max(element.clientWidth || 0, 360);
    const rowHeight = 34;
    const margin = { top: 36, right: 120, bottom: 32, left: 150 };
    const height = Math.max(margin.top + margin.bottom + data.length * rowHeight, 260);
    const maxDepth = Math.max(1, d3.max(data, queue => queue.ready + queue.unacknowledged) || 0);

    const svg = d3.select(element)
        .append('svg')
        .attr('viewBox', `0 0 ${width} ${height}`)
        .attr('role', 'img')
        .attr('aria-label', 'RabbitMQ queue depth');

    const x = d3.scaleLinear()
        .domain([0, maxDepth])
        .nice()
        .range([margin.left, width - margin.right]);

    const y = d3.scaleBand()
        .domain(data.map(queue => queue.name))
        .range([margin.top, height - margin.bottom])
        .padding(0.28);

    svg.append('g')
        .attr('class', 'axis')
        .attr('transform', `translate(0,${height - margin.bottom})`)
        .call(d3.axisBottom(x).ticks(Math.min(5, Math.max(2, width / 180))).tickFormat(d3.format('~s')));

    const rows = svg.append('g')
        .selectAll('g')
        .data(data)
        .join('g')
        .attr('transform', queue => `translate(0,${y(queue.name) || 0})`);

    rows.append('text')
        .attr('class', 'queue-label')
        .attr('x', margin.left - 12)
        .attr('y', y.bandwidth() / 2)
        .attr('text-anchor', 'end')
        .attr('dominant-baseline', 'middle')
        .text(queue => trim(queue.name, 24));

    rows.append('rect')
        .attr('class', 'queue-track')
        .attr('x', margin.left)
        .attr('y', 0)
        .attr('width', x(maxDepth) - margin.left)
        .attr('height', y.bandwidth())
        .attr('rx', 3);

    rows.append('rect')
        .attr('class', 'queue-ready')
        .attr('x', margin.left)
        .attr('y', 0)
        .attr('width', queue => Math.max(x(queue.ready) - margin.left, 0))
        .attr('height', y.bandwidth())
        .attr('rx', 3);

    rows.append('rect')
        .attr('class', 'queue-unacknowledged')
        .attr('x', queue => x(queue.ready))
        .attr('y', 0)
        .attr('width', queue => Math.max(x(queue.ready + queue.unacknowledged) - x(queue.ready), 0))
        .attr('height', y.bandwidth())
        .attr('rx', queue => queue.ready === 0 ? 3 : 0);

    rows.append('text')
        .attr('class', 'queue-value')
        .attr('x', width - margin.right + 14)
        .attr('y', y.bandwidth() / 2)
        .attr('dominant-baseline', 'middle')
        .text(queue => `${formatNumber(queue.ready + queue.unacknowledged)} total, ${formatNumber(queue.consumers)} consumers`);

    const legend = svg.append('g')
        .attr('transform', `translate(${margin.left},16)`);

    appendLegendItem(legend, 0, 'ready', 'Ready');
    appendLegendItem(legend, 86, 'unacknowledged', 'Unacknowledged');
}

function appendLineLegendItem(legend, x, className, label) {
    const item = legend.append('g')
        .attr('transform', `translate(${x},0)`);

    item.append('line')
        .attr('class', `legend-line ${className}`)
        .attr('x1', 0)
        .attr('x2', 12)
        .attr('y1', 5)
        .attr('y2', 5);

    item.append('text')
        .attr('class', 'legend-label')
        .attr('x', 18)
        .attr('y', 9)
        .text(label);
}

function appendLegendItem(legend, x, kind, label) {
    const item = legend.append('g')
        .attr('transform', `translate(${x},0)`);

    item.append('rect')
        .attr('class', `legend-swatch ${kind}`)
        .attr('width', 10)
        .attr('height', 10)
        .attr('rx', 2);

    item.append('text')
        .attr('class', 'legend-label')
        .attr('x', 16)
        .attr('y', 9)
        .text(label);
}

function trim(value, maxLength) {
    return value.length > maxLength
        ? `${value.slice(0, maxLength - 1)}...`
        : value;
}

function formatNumber(value) {
    return new Intl.NumberFormat(undefined, {
        maximumFractionDigits: 0
    }).format(value);
}

function nullableNumber(value) {
    return value === null || value === undefined
        ? null
        : Math.max(Number(value), 0);
}
