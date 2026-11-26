// The home page: plays animations without leaving the page, and keeps "On the tree now" (and which tile is lit) up to
// date as things change, whoever changes them, including the Showcase moving from one effect to the next. What's playing
// comes from the server (NowPlayingHub), as it changes.
(() => {
    const panel = document.querySelector('.now');
    const contextLabel = document.getElementById('nowContext');
    const title = document.getElementById('nowTitle');
    const detail = document.getElementById('nowDetail');
    const showcaseInfo = document.getElementById('nowShowcaseInfo');
    const progress = document.getElementById('nowProgress');
    const progressFill = document.getElementById('nowProgressFill');
    const nextLabel = document.getElementById('nowNext');
    const countdown = document.getElementById('nowCountdown');
    const showcaseMeta = document.getElementById('showcaseMeta');
    const stopButton = document.getElementById('nowStop');
    const showcaseButton = document.getElementById('nowShowcase');
    const announce = document.getElementById('nowAnnounce');
    const garland = [...document.querySelectorAll('.garland-bulb')];
    const tiles = [...document.querySelectorAll('.tiles [data-name]')];

    // What's playing, and when it arrived (its times are as of then)
    let state = JSON.parse(document.getElementById('nowPlaying').textContent);
    let received = performance.now();
    let problem = null;

    const tileFor = name => tiles.find(tile => tile.dataset.name === name);
    const colours = name => tileFor(name)?.dataset.lights.split(',') ?? ['#fff1d0'];
    const secondsSince = seconds => seconds + (performance.now() - received) / 1000;

    function update(newState) {
        if (newState.name !== state.name) {
            announce.textContent = newState.name ? `${newState.label} is playing.` : 'Nothing is playing.';
        } else if (newState.showcase?.name && newState.showcase.name !== state.showcase?.name) {
            announce.textContent = `Showcase is now showing ${newState.showcase.label}.`;
        }
        state = newState;
        received = performance.now();
        problem = null;
        render();
    }

    function render() {
        const playing = state.name;
        const showcase = state.showcase;
        const nextName = showcase && showcase.nextName !== showcase.name ? showcase.nextName : null;
        for (const tile of tiles) {
            const isPlaying = tile.dataset.name === playing;
            const isShowing = !!showcase && tile.dataset.name === showcase.name;
            const isNext = !!nextName && tile.dataset.name === nextName;
            tile.classList.toggle('is-playing', isPlaying);
            tile.classList.toggle('is-showing', isShowing);
            tile.classList.toggle('is-next', isNext);
            tile.classList.remove('is-starting');
            if (isPlaying) {
                tile.setAttribute('aria-current', 'true');
            } else {
                tile.removeAttribute('aria-current');
            }
            tile.querySelector('.tile-state').textContent = isPlaying ? 'Playing' : isShowing ? 'On now' : isNext ? 'Up next' : '';
        }

        panel.classList.toggle('is-lit', !!playing);
        stopButton.hidden = !playing;
        showcaseButton.hidden = !!playing;
        contextLabel.textContent = playing === 'Showcase' ? 'Showcase · On the tree now' : 'On the tree now';
        title.textContent = !playing ? 'Nothing from here' : showcase ? showcase.label : state.label;
        const lights = colours(showcase ? showcase.name : playing);
        garland.forEach((bulb, i) => bulb.style.setProperty('--c', lights[i % lights.length]));
        tick();
    }

    function formatCountdown(seconds) {
        const s = Math.max(0, Math.ceil(seconds));
        if (s < 60) return `${s}s`;
        const m = Math.floor(s / 60), rem = s % 60;
        return rem ? `${m}m ${rem}s` : `${m}m`;
    }

    // The parts that change as time goes on: how long it's been playing, or the Showcase's countdown to its next effect
    function tick() {
        const showcase = state.showcase;
        const showShowcase = !!showcase && !problem;
        showcaseInfo.classList.toggle('is-on', showShowcase);
        progress.classList.toggle('is-on', showShowcase);
        if (problem) {
            detail.textContent = problem;
        } else if (!state.name) {
            detail.textContent = 'Pick something below, or play the Showcase.';
        } else if (state.name === 'Showcase' && !showcase) {
            detail.textContent = 'Starting the Showcase.';
        } else if (showcase) {
            const shown = secondsSince(showcase.elapsedSeconds);
            const left = Math.max(0, Math.ceil(showcase.seconds - shown));
            const next = showcase.nextName && showcase.nextName !== showcase.name ? showcase.nextLabel : null;
            detail.textContent = left <= 0
                ? (next ? `Changing to ${next} now.` : 'Changing now.')
                : next
                    ? `Currently showing ${showcase.label}; changing to ${next} in ${left} second${left === 1 ? '' : 's'}.`
                    : `Currently showing ${showcase.label}; changing in ${left} second${left === 1 ? '' : 's'}.`;
            progressFill.style.width = `${Math.min(100, Math.max(0, shown / showcase.seconds * 100))}%`;
            if (next) {
                const strong = document.createElement('strong');
                strong.textContent = next;
                nextLabel.replaceChildren(document.createTextNode('Next: '), strong);
            } else {
                nextLabel.textContent = `Repeating ${showcase.label}`;
            }
            countdown.textContent = left <= 0 ? 'Changing…' : `${formatCountdown(left)} left`;
            if (showcaseMeta) {
                showcaseMeta.textContent = next
                    ? `Now: ${showcase.label} · Next: ${next} (${formatCountdown(left)})`
                    : `Now: ${showcase.label} (${formatCountdown(left)})`;
            }
            return;
        } else {
            detail.textContent = playingFor(secondsSince(state.elapsedSeconds));
        }

        if (showcaseMeta && showcaseMeta.dataset.default) {
            showcaseMeta.textContent = showcaseMeta.dataset.default;
        }
    }

    function playingFor(seconds) {
        const minutes = Math.round(seconds / 60);
        if (seconds < 45) return 'Just started.';
        if (minutes < 2) return 'Playing for a minute.';
        if (minutes < 60) return `Playing for ${minutes} minutes.`;
        const hours = Math.floor(minutes / 60), rest = minutes % 60;
        return `Playing for ${hours} hour${hours === 1 ? '' : 's'}${rest ? ` ${rest} minute${rest === 1 ? '' : 's'}` : ''}.`;
    }

    async function post(url, body) {
        const response = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body ?? {}) });
        if (!response.ok) throw new Error((await response.text()) || response.statusText);
        return response.json();
    }

    async function play(name, text, tile) {
        tile?.classList.add('is-starting');
        try {
            update(await post('/Home/Play', { name, text }));
        } catch (err) {
            tile?.classList.remove('is-starting');
            problem = `Couldn't start it: ${err.message}`;
            tick();
        }
    }

    // Each tile is a submit button (so it works without this script too): play it here instead
    for (const form of document.querySelectorAll('form.tiles')) {
        form.addEventListener('submit', event => {
            const button = event.submitter;
            if (!button?.dataset.play) return;
            event.preventDefault();
            const tile = button.closest('[data-name]');
            const text = tile?.querySelector('.message-input')?.value;
            play(button.dataset.play, text, tile);
        });
    }

    showcaseButton.addEventListener('click', () => play('Showcase', null, tileFor('Showcase')));

    stopButton.addEventListener('click', async () => {
        try {
            update(await post('/Home/Stop'));
        } catch (err) {
            problem = `Couldn't stop it: ${err.message}`;
            tick();
        }
    });

    for (const link of document.querySelectorAll('[data-confirm]')) {
        link.addEventListener('click', event => {
            if (!confirm(link.dataset.confirm)) event.preventDefault();
        });
    }

    // Kept up to date by the server; if that can't connect, by asking every few seconds
    async function refresh() {
        try {
            update(await (await fetch('/Home/CurrentlyPlaying')).json());
        } catch { /* (tried again later) */ }
    }

    if (window.signalR) {
        const connection = new signalR.HubConnectionBuilder()
            .withUrl('/nowplayinghub')
            .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
            .build();
        connection.on('NowPlaying', update);
        connection.onreconnected(refresh);
        const start = () => connection.start().catch(() => setTimeout(start, 10000));
        connection.onclose(() => setTimeout(start, 10000));
        start();
    } else {
        setInterval(refresh, 5000);
    }
    // (A phone that's been asleep catches up straight away)
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') refresh();
    });

    setInterval(tick, 500);
    render();
})();
