// Tree image capture page.
//
// This page owns the camera. The server (ImageCaptureCoordinator) lights the tree one LED at a time and, over SignalR, asks
// this page for a frame of each one. For each request the page:
//   1. waits until the settle delay has passed since the request arrived,
//   2. grabs the first *new* camera frame after that (so it can never use a stale frame),
//   3. replies straight away so the server can light the next LED, then
//   4. encodes the PNG and uploads it in the background, while the next LED is being captured.
// If the connection drops the page reconnects and re-registers with the same client id (kept for the tab, even across
// reloads), and the server carries on from where it was.
(() => {
    'use strict';

    const config = window.imageCaptureConfig;
    const el = id => document.getElementById(id);
    const video = el('camera');

    const FRAME_TIMEOUT_MS = 4000; // how long to wait for a new camera frame once the settle delay is up
    const HIDDEN_MESSAGE = "The capture tab is hidden (or minimised), so the camera preview isn't updating. Bring it back into view to carry on.";
    const hasFrameCallbacks = 'requestVideoFrameCallback' in HTMLVideoElement.prototype;

    // ---------------------------------------------------------------- helpers

    const settings = {
        get(key, fallback) {
            try {
                const value = localStorage.getItem('imageCapture.' + key);
                return value === null ? fallback : JSON.parse(value);
            } catch {
                return fallback;
            }
        },
        set(key, value) {
            try {
                localStorage.setItem('imageCapture.' + key, JSON.stringify(value));
            } catch {
                // Storage isn't available; settings just won't be remembered
            }
        }
    };

    const clientId = (() => {
        const make = () => (window.crypto && crypto.randomUUID) ? crypto.randomUUID() : Date.now().toString(36) + Math.random().toString(36).slice(2);
        try {
            let id = sessionStorage.getItem('imageCapture.clientId');
            if (!id) {
                id = make();
                sessionStorage.setItem('imageCapture.clientId', id);
            }
            return id;
        } catch {
            return make();
        }
    })();

    const pageId = Date.now().toString(36) + Math.random().toString(36).slice(2); // changes every time the page loads

    const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

    function clampInput(input) {
        const value = Math.round(Number(input.value));
        const clamped = Number.isFinite(value) ? Math.min(Number(input.max), Math.max(Number(input.min), value)) : Number(input.defaultValue);
        input.value = clamped;
        return clamped;
    }

    function formatSeconds(seconds) {
        seconds = Math.round(seconds);
        return seconds >= 60 ? `${Math.floor(seconds / 60)} min ${seconds % 60} s` : `${seconds} s`;
    }

    function log(message) {
        const item = document.createElement('li');
        item.textContent = `${new Date().toLocaleTimeString()} ${message}`;
        const list = el('log');
        list.prepend(item);
        while (list.children.length > 8) list.lastChild.remove();
    }

    async function postJson(url, body) {
        return fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: body === undefined ? undefined : JSON.stringify(body)
        });
    }

    async function errorText(response) {
        try {
            const body = await response.json();
            return body.error || response.statusText;
        } catch {
            return response.statusText || `HTTP ${response.status}`;
        }
    }

    // ---------------------------------------------------------------- camera

    let stream = null;
    let cameraReady = false;

    function setCameraInfo(text, isProblem) {
        el('cameraInfo').textContent = text;
        el('cameraInfo').className = 'small text-break ' + (isProblem ? 'text-danger' : 'text-muted');
    }

    async function startCamera() {
        cameraReady = false;
        updateButtons();
        if (stream) {
            stream.getTracks().forEach(track => track.stop());
            stream = null;
        }

        // With nothing selected this asks for exactly what the page always has: the browser's default camera and resolution
        const deviceId = el('cameraSelect').value;
        const resolution = el('resolutionSelect').value;
        let constraints = true;
        if (deviceId || resolution) {
            constraints = {};
            if (deviceId) constraints.deviceId = { exact: deviceId };
            if (resolution) {
                const [width, height] = resolution.split('x').map(Number);
                constraints.width = { ideal: width };
                constraints.height = { ideal: height };
            }
        }

        setCameraInfo('Starting the camera…');
        try {
            stream = await navigator.mediaDevices.getUserMedia({ video: constraints, audio: false });
        } catch (error) {
            if (deviceId && (error.name === 'OverconstrainedError' || error.name === 'NotFoundError')) {
                // The remembered camera isn't there any more: fall back to the default one
                el('cameraSelect').value = '';
                settings.set('camera', '');
                return startCamera();
            }
            setCameraInfo(`Couldn't start the camera: ${error.message}`, true);
            return;
        }

        const track = stream.getVideoTracks()[0];
        track.addEventListener('ended', () => {
            cameraReady = false;
            setCameraInfo('The camera has stopped (was it unplugged?). Reload the page to restart it.', true);
            updateButtons();
        });

        video.srcObject = stream;
        try {
            await video.play();
        } catch {
            // Autoplay is muted so should be allowed; the 'autoplay' attribute will start it anyway
        }
        for (let i = 0; i < 100 && !video.videoWidth; i++) await sleep(50);

        const info = track.getSettings();
        const label = (track.label || 'Camera').slice(0, 80);
        setCameraInfo(`${label}: ${video.videoWidth} × ${video.videoHeight}` + (info.frameRate ? ` at ${Math.round(info.frameRate)} fps` : ''));
        cameraReady = video.videoWidth > 0;
        updateButtons();
        await listCameras();
    }

    async function listCameras() {
        try {
            const select = el('cameraSelect');
            const current = select.value;
            const cameras = (await navigator.mediaDevices.enumerateDevices()).filter(device => device.kind === 'videoinput');
            select.replaceChildren(new Option('Default camera', ''));
            cameras.forEach((camera, index) => select.add(new Option(camera.label || `Camera ${index + 1}`, camera.deviceId)));
            select.value = cameras.some(camera => camera.deviceId === current) ? current : '';
        } catch {
            // Can't list cameras; the default one still works
        }
    }

    // ---------------------------------------------------------------- frame grabbing

    const grabCanvas = document.createElement('canvas');
    const grabContext = grabCanvas.getContext('2d');

    // Copies the video's current frame. The PNG is encoded in the background: toBlob takes its own copy of the canvas
    // immediately, so the canvas can be reused for the next frame straight away.
    function snapshot() {
        const width = video.videoWidth;
        const height = video.videoHeight;
        if (!width || !height) {
            throw new Error("The camera isn't delivering frames.");
        }
        if (grabCanvas.width !== width) grabCanvas.width = width;
        if (grabCanvas.height !== height) grabCanvas.height = height;
        grabContext.drawImage(video, 0, 0, width, height);
        const png = new Promise(resolve => grabCanvas.toBlob(resolve, 'image/png'));
        return { width, height, png };
    }

    // Resolves with a snapshot of the first frame the camera delivers at or after notBefore (a performance.now() time).
    function grabFrame(notBefore, isStale) {
        return new Promise((resolve, reject) => {
            let finished = false;
            const finish = (callback, value) => {
                if (!finished) {
                    finished = true;
                    clearTimeout(timer);
                    callback(value);
                }
            };
            const take = presentedAt => {
                try {
                    finish(resolve, { ...snapshot(), presentedAt });
                } catch (error) {
                    finish(reject, error);
                }
            };
            const timer = setTimeout(() => finish(reject, new Error(document.hidden ? HIDDEN_MESSAGE : 'No new frames are coming from the camera.')),
                Math.max(0, notBefore - performance.now()) + FRAME_TIMEOUT_MS);

            if (hasFrameCallbacks) {
                const onFrame = (now, metadata) => {
                    if (finished) return;
                    if (isStale()) return finish(reject, new Error('Superseded by a newer request.'));
                    const presentedAt = metadata && metadata.presentationTime ? metadata.presentationTime : now;
                    if (presentedAt >= notBefore) {
                        take(presentedAt); // Must happen inside the callback, while this is the video's current frame
                    } else {
                        video.requestVideoFrameCallback(onFrame);
                    }
                };
                video.requestVideoFrameCallback(onFrame);
            } else {
                // Browsers without frame callbacks: take whatever's showing once the delay is up (as the page always used to)
                setTimeout(() => take(performance.now()), Math.max(0, notBefore - performance.now()));
            }
        });
    }

    // ---------------------------------------------------------------- uploads

    const uploads = {
        queue: [],
        active: 0,
        maxConcurrent: 2,
        maxPending: 6, // stop grabbing new frames if this many are still waiting to be encoded/uploaded
        waiters: [],
        get pending() { return this.queue.length + this.active; },

        add(item) {
            this.queue.push(item);
            this.pump();
        },

        pump() {
            while (this.active < this.maxConcurrent && this.queue.length) {
                const item = this.queue.shift();
                this.active++;
                this.send(item).finally(() => {
                    this.active--;
                    this.pump();
                    this.wake();
                });
            }
        },

        async send(item) {
            const giveUp = message => {
                log(message);
                // Let the server know, so it takes the picture again rather than waiting for it
                connection.invoke('UploadFailed', item.jobId, item.name, item.seq).catch(() => { });
            };
            const blob = await item.png;
            if (!blob) {
                return giveUp(`Couldn't encode image ${item.name}; it'll be re-captured.`);
            }
            const url = `${config.uploadUrl}?jobId=${encodeURIComponent(item.jobId)}&seq=${item.seq}&name=${encodeURIComponent(item.name)}`;
            for (let attempt = 1; attempt <= 10; attempt++) { // keeps trying for about half a minute, to ride out network drops
                try {
                    const response = await fetch(url, { method: 'POST', body: blob, headers: { 'Content-Type': 'image/png' } });
                    if (response.ok) return;
                    if (response.status === 409) return; // the capture has finished or been stopped
                    if (response.status === 400 || response.status === 413) {
                        return giveUp(`Upload of ${item.name} was rejected (${await errorText(response)}); it'll be re-captured.`);
                    }
                } catch {
                    // Network problem: try again
                }
                await sleep(Math.min(500 * attempt, 5000));
            }
            giveUp(`Couldn't upload ${item.name}; it'll be re-captured.`);
        },

        waitForRoom() {
            return this.pending < this.maxPending ? Promise.resolve() : new Promise(resolve => this.waiters.push(resolve));
        },

        wake() {
            while (this.waiters.length && this.pending < this.maxPending) this.waiters.shift()();
        }
    };

    // ---------------------------------------------------------------- requests from the server

    let latestFrameRequest = 0;

    async function onCaptureFrame(request) {
        const received = performance.now();
        const token = ++latestFrameRequest; // a newer request means the server has given up on this one
        const isStale = () => token !== latestFrameRequest;
        const failed = error => ({ ok: false, width: 0, height: 0, error });

        if (document.hidden) return failed(HIDDEN_MESSAGE);
        if (!cameraReady || !video.videoWidth) return failed("The camera isn't ready.");

        await uploads.waitForRoom();
        if (isStale()) return failed('Superseded by a newer request.');
        try {
            const frame = await grabFrame(received + request.settleDelayMs, isStale);
            uploads.add({ jobId: request.jobId, seq: request.seq, name: request.name, png: frame.png });
            return { ok: true, width: frame.width, height: frame.height, error: null };
        } catch (error) {
            return failed(error.message);
        }
    }

    // Camera latency: samples the average brightness of every frame while the server switches the whole tree on/off.
    const SAMPLE_WIDTH = 160, SAMPLE_HEIGHT = 120;
    const latency = {
        canvas: null,
        context: null,
        sampling: false,
        generation: 0,
        samples: [],
        armedAt: 0,
        baseline: 0,
        noise: 0,

        startSampling() {
            if (!this.canvas) {
                this.canvas = document.createElement('canvas');
                this.canvas.width = SAMPLE_WIDTH;
                this.canvas.height = SAMPLE_HEIGHT;
                this.context = this.canvas.getContext('2d', { willReadFrequently: true });
                this.context.imageSmoothingEnabled = true;
                this.context.imageSmoothingQuality = 'high'; // average when scaling down, so small LEDs aren't skipped
            }
            if (this.sampling) return;
            this.sampling = true;
            const generation = ++this.generation;
            const onFrame = (now, metadata) => {
                if (!this.sampling || generation !== this.generation) return;
                this.context.drawImage(video, 0, 0, SAMPLE_WIDTH, SAMPLE_HEIGHT);
                const data = this.context.getImageData(0, 0, SAMPLE_WIDTH, SAMPLE_HEIGHT).data;
                let sum = 0;
                for (let i = 0; i < data.length; i += 4) sum += data[i] + data[i + 1] + data[i + 2];
                this.samples.push({ t: metadata && metadata.presentationTime ? metadata.presentationTime : now, level: sum / (SAMPLE_WIDTH * SAMPLE_HEIGHT * 3) });
                video.requestVideoFrameCallback(onFrame);
            };
            video.requestVideoFrameCallback(onFrame);
        },

        async arm() {
            if (!hasFrameCallbacks) {
                return { ok: false, width: 0, height: 0, error: "This browser can't measure camera latency (no requestVideoFrameCallback support)." };
            }
            if (document.hidden) return { ok: false, width: 0, height: 0, error: HIDDEN_MESSAGE };
            this.samples = [];
            this.startSampling();
            for (let i = 0; i < 60 && this.samples.length < 5; i++) await sleep(50);
            if (this.samples.length < 5) return { ok: false, width: 0, height: 0, error: 'No frames are coming from the camera.' };
            this.armedAt = performance.now();
            const levels = this.samples.slice(-4).map(sample => sample.level);
            this.baseline = levels.reduce((total, level) => total + level, 0) / levels.length;
            this.noise = Math.max(...levels) - Math.min(...levels); // how much the brightness wanders with nothing changing
            return { ok: true, width: video.videoWidth, height: video.videoHeight, error: null };
        },

        async measure(request) {
            const received = performance.now();
            await sleep(request.windowMs);
            this.sampling = false;
            const fail = error => ({ ok: false, firstChangeMs: 0, fullChangeMs: 0, frameIntervalMs: 0, change: 0, error });

            const samples = this.samples.filter(sample => sample.t > this.armedAt);
            if (samples.length < 5) return fail('Too few camera frames to measure with.');
            const changes = samples.map(sample => Math.abs(sample.level - this.baseline));
            const biggest = Math.max(...changes);
            if (biggest < Math.max(0.4, 6 * this.noise)) {
                return fail("Couldn't see the tree change clearly enough. Make sure the camera can see the tree, then try again.");
            }

            // The first frame that's mostly changed, and the first that's (all but) completely changed. The change peaks before
            // the camera's auto-exposure starts compensating, so the peak is a fully changed frame.
            const first = samples[changes.findIndex(change => change >= 0.5 * biggest)];
            const full = samples[changes.findIndex(change => change >= 0.85 * biggest)];
            const intervals = samples.slice(1).map((sample, i) => sample.t - samples[i].t).sort((a, b) => a - b);
            return {
                ok: true,
                firstChangeMs: first.t - received,
                fullChangeMs: full.t - received,
                frameIntervalMs: intervals[Math.floor(intervals.length / 2)],
                change: biggest,
                error: null
            };
        }
    };

    // ---------------------------------------------------------------- status

    let status = null;
    let summary = null;
    let lastStatusVersion = 0;
    let lastCompletedJob = null;
    let connected = false;
    let wakeLockRequested = false;

    const isActive = s => !!s && (s.state === 'Queued' || s.state === 'Running');
    const isMine = s => !!s && s.cameraClientId === clientId;

    function renderStatus(newStatus) {
        if (!newStatus || (newStatus.version && newStatus.version < lastStatusVersion)) return;
        lastStatusVersion = newStatus.version || lastStatusVersion;
        const firstStatus = status === null;
        status = newStatus;

        const active = isActive(status);
        const isCapture = status.kind === 'Capture';
        const fraction = status.total ? status.done / status.total : 0;
        const bar = el('progressBar');
        bar.style.width = `${(active || status.state === 'Completed') ? Math.round(fraction * 100) : 0}%`;
        bar.className = 'progress-bar' + (active && !status.paused ? ' progress-bar-striped progress-bar-animated' : '') +
            (status.paused ? ' bg-warning' : status.state === 'Failed' ? ' bg-danger' : '');

        let progress = '';
        if (status.state !== 'Idle' && status.total) {
            progress = isCapture
                ? `${status.direction}°: ${status.done} of ${status.total} images`
                : `Latency measurement: ${status.done} of ${status.total}`;
            if (isCapture && active && status.secondsPerImage) {
                progress += ` · ${status.secondsPerImage.toFixed(2)} s per image · about ${formatSeconds(status.etaSeconds || 0)} left`;
            }
        }
        el('progressText').textContent = progress || '\u00a0';

        const message = el('statusMessage');
        message.textContent = status.state === 'Idle' ? 'Ready.' : status.message;
        message.className = 'alert small py-2 mt-2 mb-2 alert-' + (
            status.state === 'Failed' ? 'danger'
                : status.state === 'Completed' ? 'success'
                    : status.state === 'Cancelled' || status.paused ? 'warning'
                        : active ? 'info' : 'secondary');

        if (active && !isMine(status)) {
            message.textContent += ' (This is being run from another capture page.)';
        }

        if (status.kind === 'Latency' && status.state === 'Completed' && status.latency) {
            showLatencyResult(status.latency);
        }

        if (isCapture && status.state === 'Completed' && status.jobId !== lastCompletedJob) {
            // Move on to the next direction when a capture finishes (but not just because the page has loaded)
            lastCompletedJob = status.jobId;
            const capturedDirection = status.direction;
            refreshSummary().then(() => { if (!firstStatus) selectNextDirection(capturedDirection); });
        } else if (!active && (status.state === 'Cancelled' || status.state === 'Failed')) {
            refreshSummary();
        }

        keepScreenOn(active && isMine(status));
        updateTitle();
        updateButtons();
    }

    function showLatencyResult(result) {
        const span = el('latencyResult');
        const fps = result.frameIntervalMs > 0 ? ` (camera ~${Math.round(1000 / result.frameIntervalMs)} fps)` : '';
        span.replaceChildren(document.createTextNode(
            `Fully visible after ${Math.round(Math.max(0, result.fullChangeMs))} ms${fps}. Suggested: ${result.suggestedSettleDelayMs} ms `));
        const use = document.createElement('button');
        use.type = 'button';
        use.className = 'btn btn-link btn-sm p-0 align-baseline';
        use.textContent = 'use this';
        use.addEventListener('click', () => {
            el('settleDelay').value = result.suggestedSettleDelayMs;
            settings.set('settleDelay', result.suggestedSettleDelayMs);
        });
        span.append(use);
    }

    function updateTitle() {
        const base = 'Tree Image Capture';
        if (isActive(status) && status.kind === 'Capture' && status.total) {
            const percent = Math.floor(100 * status.done / status.total);
            document.title = `${status.paused ? '⏸ ' : ''}${percent}% · ${base}`;
        } else {
            document.title = `${base} - TreeLightsWeb`;
        }
    }

    function updateButtons() {
        const active = isActive(status);
        const canStart = connected && cameraReady && !active;
        const staged = directionSummary(Number(el('direction').value));
        el('startButton').disabled = !canStart;
        el('measureButton').disabled = !canStart;
        el('stopButton').disabled = !active;
        el('lightTreeButton').disabled = active;
        const resume = el('resumeButton');
        const canResume = !!staged && staged.stagedImages > 0;
        resume.classList.toggle('d-none', !canResume);
        resume.disabled = !canStart || !canResume;
        resume.textContent = canResume ? `Resume (${staged.stagedImages} of ${staged.stagedTotal} done)` : 'Resume';
        ['cameraSelect', 'resolutionSelect', 'direction'].forEach(id => el(id).disabled = active && isMine(status));
    }

    // ---------------------------------------------------------------- directions summary

    const directionSummary = direction => summary && summary.directions.find(d => d.direction === direction);

    async function refreshSummary() {
        try {
            const response = await fetch(config.summaryUrl, { cache: 'no-store' });
            if (!response.ok) return;
            summary = await response.json();
        } catch {
            return;
        }

        const body = el('directionsTable').tBodies[0];
        body.replaceChildren();
        for (const d of summary.directions) {
            const row = body.insertRow();
            row.style.cursor = 'pointer';
            row.addEventListener('click', () => selectDirection(d.direction));
            if (d.direction === Number(el('direction').value)) row.classList.add('table-active');
            row.insertCell().textContent = `${d.direction}°`;
            let images = d.images ? `${d.images}${summary.ledCount ? ' / ' + summary.ledCount : ''}` : '–';
            if (d.complete) images += ' ✓';
            if (d.stagedImages) images += ` (partial capture: ${d.stagedImages} / ${d.stagedTotal})`;
            row.insertCell().textContent = images;
            row.insertCell().textContent = d.captured ? new Date(d.captured).toLocaleString() + (d.width ? ` · ${d.width}×${d.height}` : '') : '';
        }
        updateButtons();
    }

    function selectDirection(direction) {
        el('direction').value = direction;
        settings.set('direction', direction);
        refreshSummary();
    }

    function selectNextDirection(justCaptured) {
        if (!summary) return;
        const all = summary.directions.map(d => d.direction);
        const start = all.indexOf(justCaptured);
        for (let i = 1; i <= all.length; i++) {
            const candidate = summary.directions[(start + i) % all.length];
            if (!candidate.complete) {
                selectDirection(candidate.direction);
                log(`Done. Rotate the tree to ${candidate.direction}° (the whole tree, not just the trunk), then capture again.`);
                return;
            }
        }
        log('All directions have been captured. You can process the images now.');
    }

    // ---------------------------------------------------------------- actions

    async function startCapture(resume, allowResolutionMismatch = false) {
        const direction = Number(el('direction').value);
        const existing = directionSummary(direction);
        if (!resume && !allowResolutionMismatch) {
            if (existing && existing.stagedImages > 0 &&
                !confirm(`There's a partial capture for ${direction}° (${existing.stagedImages} of ${existing.stagedTotal} images). Start again and discard it?`)) {
                return;
            }
            if (existing && existing.images > 0 &&
                !confirm(`${direction}° has already been captured${existing.captured ? ' (' + new Date(existing.captured).toLocaleString() + ')' : ''}. ` +
                    'Capture it again? The existing images are only replaced once the new capture completes.')) {
                return;
            }
        }

        settings.set('settleDelay', clampInput(el('settleDelay')));
        settings.set('exposureSettle', clampInput(el('exposureSettle')));
        el('startButton').disabled = true;
        const response = await postJson(config.startUrl, {
            clientId,
            direction,
            settleDelayMs: clampInput(el('settleDelay')),
            exposureSettleMs: clampInput(el('exposureSettle')),
            resume,
            width: video.videoWidth,
            height: video.videoHeight,
            allowResolutionMismatch
        }).catch(() => null);
        if (response && response.ok) return;

        const error = response ? await response.json().catch(() => ({})) : { error: "Couldn't reach the server." };
        if (error.code === 'resolution-mismatch' &&
            confirm(`${error.error}\n\nCapture at ${video.videoWidth}×${video.videoHeight} anyway?`)) {
            return startCapture(resume, true);
        }
        showProblem(error.error || `Couldn't start the capture (HTTP ${response && response.status}).`);
        updateButtons();
    }

    async function measureLatency() {
        el('latencyResult').textContent = '';
        const response = await postJson(config.measureLatencyUrl, { clientId }).catch(() => null);
        if (!response || !response.ok) {
            showProblem(response ? await errorText(response) : "Couldn't reach the server.");
        }
    }

    function showProblem(text) {
        const message = el('statusMessage');
        message.textContent = text;
        message.className = 'alert alert-danger small py-2 mt-2 mb-2';
    }

    // ---------------------------------------------------------------- keep the screen on / warn before leaving

    let wakeLock = null;
    async function keepScreenOn(on) {
        try {
            if (on && !wakeLock && !wakeLockRequested && 'wakeLock' in navigator && document.visibilityState === 'visible') {
                wakeLockRequested = true;
                try {
                    wakeLock = await navigator.wakeLock.request('screen');
                    wakeLock.addEventListener('release', () => { wakeLock = null; });
                } finally {
                    wakeLockRequested = false;
                }
            } else if (!on && wakeLock) {
                const lock = wakeLock;
                wakeLock = null;
                await lock.release();
            }
        } catch {
            // Not supported or not allowed; the screen might sleep, which pauses the capture until it's woken
        }
    }

    document.addEventListener('visibilitychange', () => {
        keepScreenOn(isActive(status) && isMine(status));
        updateTitle();
    });

    window.addEventListener('beforeunload', event => {
        if ((isActive(status) && isMine(status)) || uploads.pending) {
            event.preventDefault();
            event.returnValue = '';
        }
    });

    // ---------------------------------------------------------------- SignalR

    const connection = new signalR.HubConnectionBuilder()
        .withUrl(config.hubUrl)
        .withAutomaticReconnect({ nextRetryDelayInMilliseconds: context => Math.min(500 + 1000 * context.previousRetryCount, 5000) })
        .withStatefulReconnect()
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    connection.on('CaptureStatus', renderStatus);
    connection.on('CaptureFrame', onCaptureFrame);
    connection.on('LatencyArm', () => latency.arm());
    connection.on('LatencyMeasure', request => latency.measure(request));

    connection.onreconnecting(() => {
        connected = false;
        el('connectionInfo').textContent = 'Lost the connection to the server; reconnecting…';
        updateButtons();
    });
    connection.onreconnected(() => register());
    connection.onclose(() => {
        connected = false;
        updateButtons();
        setTimeout(connect, 2000);
    });

    async function connect() {
        try {
            await connection.start();
            await register();
        } catch {
            el('connectionInfo').textContent = "Can't reach the server; retrying…";
            setTimeout(connect, 3000);
        }
    }

    async function register() {
        try {
            const current = await connection.invoke('RegisterCamera', clientId, pageId);
            connected = true;
            el('connectionInfo').textContent = '';
            renderStatus(current);
            refreshSummary(); // things may have moved on while we were disconnected (e.g. the server restarted)
        } catch {
            connected = false;
            setTimeout(register, 2000);
        }
        updateButtons();
    }

    // ---------------------------------------------------------------- wire up the page

    el('settleDelay').value = settings.get('settleDelay', el('settleDelay').value);
    el('exposureSettle').value = settings.get('exposureSettle', el('exposureSettle').value);
    el('resolutionSelect').value = settings.get('resolution', '');
    el('direction').value = settings.get('direction', 0);
    if (!el('direction').value) el('direction').value = 0;
    const savedCamera = settings.get('camera', '');
    if (savedCamera) el('cameraSelect').add(new Option('Saved camera', savedCamera, true, true));

    el('startButton').addEventListener('click', () => startCapture(false));
    el('resumeButton').addEventListener('click', () => startCapture(true));
    el('stopButton').addEventListener('click', () => postJson(config.stopUrl).catch(() => showProblem("Couldn't reach the server.")));
    el('measureButton').addEventListener('click', measureLatency);
    el('lightTreeButton').addEventListener('click', () => postJson(config.lightTreeUrl).catch(() => showProblem("Couldn't reach the server.")));
    el('direction').addEventListener('change', () => selectDirection(Number(el('direction').value)));
    el('settleDelay').addEventListener('change', () => settings.set('settleDelay', clampInput(el('settleDelay'))));
    el('exposureSettle').addEventListener('change', () => settings.set('exposureSettle', clampInput(el('exposureSettle'))));
    el('cameraSelect').addEventListener('change', () => { settings.set('camera', el('cameraSelect').value); startCamera(); });
    el('resolutionSelect').addEventListener('change', () => { settings.set('resolution', el('resolutionSelect').value); startCamera(); });

    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
        setCameraInfo('This browser can only use the camera on a secure (https or localhost) page.', true);
    } else {
        startCamera();
    }
    refreshSummary();
    connect();
})();
