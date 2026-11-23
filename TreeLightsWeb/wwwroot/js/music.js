// Plays a music file (or listens through the microphone), measures how loud the bass, middle and treble are and spots the
// beats, and sends that to the server about 30 times a second for the music-reactive animation (which it starts).
(() => {
    const fileInput = document.getElementById('musicFile');
    const listenButton = document.getElementById('listen');
    const player = document.getElementById('player');
    const status = document.getElementById('status');
    const bars = { bass: document.getElementById('bassBar'), mid: document.getElementById('midBar'), treble: document.getElementById('trebleBar') };
    const beatLight = document.getElementById('beatLight');
    const beatCount = document.getElementById('beatCount');

    const connection = new signalR.HubConnectionBuilder().withUrl('/treehub').withAutomaticReconnect().build();
    connection.start().catch(err => { status.textContent = "Couldn't connect to the server: " + err; });

    let context = null, analyser = null, bins = null;
    let fileSource = null, micSource = null, micStream = null, listeningTo = null;
    let animationStarted = false;

    function startAudio() {
        if (!context) {
            context = new (window.AudioContext || window.webkitAudioContext)();
            analyser = context.createAnalyser();
            analyser.fftSize = 2048;
            analyser.smoothingTimeConstant = 0.5;
            bins = new Uint8Array(analyser.frequencyBinCount);
            setInterval(measure, 33);
        }
        if (context.state === 'suspended') {
            context.resume();
        }
    }

    function startTreeAnimation() {
        if (!animationStarted) {
            animationStarted = true;
            fetch('/Home/MusicReactive').catch(() => { animationStarted = false; });
        }
    }

    // Analyses just the given source (the music file or the microphone)
    function listenTo(source) {
        if (listeningTo && listeningTo !== source) {
            try { listeningTo.disconnect(analyser); } catch { }
        }
        if (listeningTo !== source) {
            source.connect(analyser);
        }
        listeningTo = source;
    }

    function stopMicrophone() {
        if (micStream) {
            micStream.getTracks().forEach(track => track.stop());
            micStream = null;
        }
        if (micSource) {
            try { micSource.disconnect(); } catch { }
            if (listeningTo === micSource) listeningTo = null;
            micSource = null;
        }
    }

    function useFilePlayer() {
        startAudio();
        stopMicrophone();
        if (!fileSource) {
            fileSource = context.createMediaElementSource(player);
            fileSource.connect(context.destination); // so it can still be heard
        }
        listenTo(fileSource);
        startTreeAnimation();
    }

    fileInput.addEventListener('change', () => {
        const file = fileInput.files[0];
        if (!file) return;
        useFilePlayer();
        player.src = URL.createObjectURL(file);
        player.play();
        status.textContent = 'Playing ' + file.name + ' (keep this page open in front, or the browser slows it down)';
    });
    player.addEventListener('play', useFilePlayer);

    listenButton.addEventListener('click', async () => {
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
            status.textContent = "The browser will only let this page use the microphone when it's opened over https (or on localhost).";
            return;
        }
        startAudio();
        try {
            // Without the processing meant for voice calls, which would flatten the music
            micStream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: false, noiseSuppression: false, autoGainControl: false } });
        } catch (err) {
            status.textContent = "Couldn't use the microphone: " + err.message;
            return;
        }
        player.pause();
        micSource = context.createMediaStreamSource(micStream);
        listenTo(micSource); // (not connected to the speakers, which would feed back)
        startTreeAnimation();
        status.textContent = 'Listening through the microphone (keep this page open in front)';
    });

    // Each band's level is relative to its recent peak, so quiet and loud music both use the whole range
    const peaks = { bass: 0.05, mid: 0.05, treble: 0.05 };
    let bassAverage = 0, lastBeat = 0, beats = 0;

    function band(lowHz, highHz) {
        const hzPerBin = context.sampleRate / analyser.fftSize;
        const from = Math.max(1, Math.floor(lowHz / hzPerBin));
        const to = Math.min(bins.length - 1, Math.ceil(highHz / hzPerBin));
        let sum = 0;
        for (let i = from; i <= to; i++) sum += bins[i] / 255;
        return sum / (to - from + 1);
    }

    function relative(name, value) {
        peaks[name] = Math.max(value, peaks[name] * 0.995, 0.05);
        return Math.min(1, value / peaks[name]);
    }

    function measure() {
        const hearing = (listeningTo === fileSource && fileSource && !player.paused) || (listeningTo === micSource && micSource);
        if (!hearing) {
            return; // nothing's sent, so the tree goes back to waiting for music
        }
        analyser.getByteFrequencyData(bins);
        const bassRaw = band(30, 150), midRaw = band(150, 2000), trebleRaw = band(2000, 8000);
        const bass = Math.pow(relative('bass', bassRaw), 1.5);
        const mid = relative('mid', midRaw);
        const treble = Math.pow(relative('treble', trebleRaw), 1.5);

        // A beat is the bass jumping well above its average over the last second or so
        const now = performance.now();
        if (bassRaw > bassAverage * 1.25 + 0.02 && bass > 0.5 && now - lastBeat > 280) {
            lastBeat = now;
            beats++;
            beatCount.textContent = beats;
            beatLight.style.backgroundColor = '#212529';
            setTimeout(() => { beatLight.style.backgroundColor = ''; }, 120);
        }
        bassAverage = bassAverage * 0.95 + bassRaw * 0.05;

        bars.bass.style.width = (bass * 100).toFixed(0) + '%';
        bars.mid.style.width = (mid * 100).toFixed(0) + '%';
        bars.treble.style.width = (treble * 100).toFixed(0) + '%';

        if (connection.state === signalR.HubConnectionState.Connected) {
            connection.send('SendMusicLevels', bass, mid, treble, beats).catch(() => { });
        }
    }
})();
