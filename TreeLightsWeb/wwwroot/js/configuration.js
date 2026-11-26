// The Configuration page: which side of the tree faces the room (with a guide shown on the tree), how dim a lit LED can
// be (with a test pattern), the colour palettes (switching, editing, adding, deleting), the colours of animations given
// their own, each animation's speed, and which animations the Showcase goes through. Everything's saved on the server
// (ConfigurationController), which returns the saved settings.
(() => {
    const errorBox = document.getElementById('configError');
    let settings = null;       // the saved settings
    let showcaseEffects = [];  // the animations the Showcase can include: [{ name, label }]
    let speedAnimations = [];  // the animations whose speed can be set: [{ name, label, group }]
    let speedLimits = { min: 0.25, max: 4 };
    let editing = null;        // the palette being edited: { originalName (null for a new one), palette }
    let guideOn = false;       // whether the orientation guide's been put on the tree
    let animationColours = []; // the animations that can have their own colours: [{ name, label, ownColours, roles: [{ name, kind, usual, own }] }]

    // ---- Helpers

    // Makes an element (text is always added as text, so names and messages can't inject anything)
    function el(tag, props = {}, ...children) {
        const element = document.createElement(tag);
        for (const [key, value] of Object.entries(props)) {
            if (key === 'class') element.className = value;
            else if (key === 'style') element.style.cssText = value;
            else if (key.startsWith('on')) element.addEventListener(key.slice(2), value);
            else if (value !== undefined && value !== null && value !== false) element.setAttribute(key, value === true ? '' : value);
        }
        for (const child of children.flat()) {
            if (child !== null && child !== undefined) element.append(child instanceof Node ? child : document.createTextNode(child));
        }
        return element;
    }

    function showError(message) {
        errorBox.textContent = message;
        errorBox.classList.toggle('d-none', !message);
    }

    // Runs an action from a button, leaving any error to show at the top of the page (post shows it)
    const quietly = action => (...args) => action(...args).catch(() => { });

    async function post(url, body) {
        showError('');
        const response = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
        if (!response.ok) {
            const text = await response.text();
            showError(text || `That didn't work (error ${response.status}).`);
            throw new Error(text);
        }
        return (response.headers.get('Content-Type') || '').includes('json') ? response.json() : null;
    }

    // ---- Orientation

    const frontDirection = document.getElementById('frontDirection');
    const frontDirectionValue = document.getElementById('frontDirectionValue');
    const mirrored = document.getElementById('mirrored');
    const orientationStatus = document.getElementById('orientationStatus');

    const orientation = () => ({ frontDirection: Number(frontDirection.value), mirrored: mirrored.checked });

    function describeSavedOrientation() {
        orientationStatus.textContent = `Saved: the front is ${settings.frontDirection}° round${settings.mirrored ? ', and the positions are a mirror image' : ''}.`;
    }

    let guideTimer = null;
    function orientationChanged() {
        frontDirectionValue.textContent = frontDirection.value;
        if (guideOn) {
            // Move the guide on the tree as the slider moves (but not on every pixel of movement)
            clearTimeout(guideTimer);
            guideTimer = setTimeout(() => post('/Configuration/Guide', orientation()).catch(() => { }), 120);
        }
    }
    frontDirection.addEventListener('input', orientationChanged);
    mirrored.addEventListener('change', orientationChanged);

    document.getElementById('showGuide').addEventListener('click', async () => {
        guideOn = true;
        await post('/Configuration/Guide', orientation()).catch(() => { });
        orientationStatus.textContent = 'The guide is on the tree: move the slider until the F faces you, then save.';
    });

    document.getElementById('stopGuide').addEventListener('click', async () => {
        guideOn = false;
        await fetch('/Home/StopAnimation');
        describeSavedOrientation();
    });

    document.getElementById('saveOrientation').addEventListener('click', quietly(async () => {
        settings = await post('/Configuration/Orientation', orientation());
        describeSavedOrientation();
    }));

    // ---- Dim LEDs

    const dimmestLevel = document.getElementById('dimmestLevel');
    const switchOffBelow = document.getElementById('switchOffBelow');
    const dimStatus = document.getElementById('dimStatus');
    let leds = { usesDdp: true, wledBrightness: 1 }; // how the colours are sent to WLED
    let dimTimer = null;

    function describeDimLevels() {
        const level = Number(dimmestLevel.value);
        document.getElementById('dimmestLevelValue').textContent = level === 0 ? 'off (every level is shown as it is)' : `${level} of 255`;
        document.getElementById('switchOffBelowValue').textContent = `${switchOffBelow.value}%`;
        switchOffBelow.disabled = level === 0;
    }

    function describeLeds() {
        if (!leds.usesDdp) {
            return "This only works when the colours are sent to WLED with DDP (the default), and they're being sent with its JSON API instead (TreeUseDDP is false).";
        }
        const brightness = Math.round(leds.wledBrightness * 100);
        return brightness < 100 ? `WLED's brightness is ${brightness}%, which is allowed for.` : '';
    }

    // Saved (and shown on the tree) as the sliders move, but not on every movement
    function dimLevelsChanged() {
        describeDimLevels();
        clearTimeout(dimTimer);
        dimTimer = setTimeout(quietly(async () => {
            settings = await post('/Configuration/DimmestLevel', { dimmestLevel: Number(dimmestLevel.value), switchOffBelowPercent: Number(switchOffBelow.value) });
            dimStatus.textContent = `Saved. ${describeLeds()}`;
        }), 150);
    }

    dimmestLevel.addEventListener('input', dimLevelsChanged);
    switchOffBelow.addEventListener('input', dimLevelsChanged);

    document.getElementById('showDimTest').addEventListener('click', quietly(async () => {
        leds = await post('/Configuration/DimLevelsTest', {});
        dimStatus.textContent = `The test pattern's on the tree. ${describeLeds()}`;
    }));

    document.getElementById('stopDimTest').addEventListener('click', async () => {
        await fetch('/Home/StopAnimation');
        dimStatus.textContent = describeLeds();
    });

    // ---- Palettes

    const hexToRgb = hex => { const v = parseInt(hex.slice(1), 16); return [(v >> 16) & 255, (v >> 8) & 255, v & 255]; };
    const smoothStep = (a, b, x) => { const t = Math.min(1, Math.max(0, (x - a) / (b - a))); return t * t * (3 - 2 * t); };

    // The colour position (0-1) of the way round the palette, as the animations work it out (PaletteSampler.Sample)
    function sample(colours, blend, position) {
        const n = colours.length, x = (((position % 1) + 1) % 1) * n, i = Math.floor(x);
        const a = colours[i % n], b = colours[(i + 1) % n];
        const s = blend <= 0.001 ? 0 : smoothStep(1 - blend, 1, x - i);
        return a.map((v, k) => Math.round(v + (b[k] - v) * s));
    }

    function previewBar(palette) {
        const colours = palette.colours.map(hexToRgb);
        const stops = [];
        for (let k = 0; k <= 60; k++) {
            stops.push(`rgb(${sample(colours, palette.blend, k / 60).join(',')}) ${(k / 60 * 100).toFixed(1)}%`);
        }
        return el('div', { class: 'rounded border', style: `height:22px;background:linear-gradient(to right, ${stops.join(', ')})`,
                           title: 'How the colourful animations go through the palette' });
    }

    function renderPalettes() {
        const cards = settings.palettes.map(p => editing && editing.originalName === p.name ? paletteEditor() : paletteCard(p));
        if (editing && editing.originalName === null) cards.push(paletteEditor());
        document.getElementById('palettes').replaceChildren(...cards.map(card => el('div', { class: 'col-md-6 col-lg-4' }, card)));
    }

    function paletteCard(palette) {
        const inUse = palette.name === settings.activePalette;
        return el('div', { class: 'card h-100' + (inUse ? ' border-dark border-2' : '') },
            el('div', { class: 'card-body' },
                el('div', { class: 'd-flex justify-content-between align-items-center mb-2', style: 'min-height:31px' },
                    el('h3', { class: 'h6 mb-0' }, palette.name),
                    inUse ? el('span', { class: 'badge bg-dark' }, 'In use')
                          : el('button', { type: 'button', class: 'btn btn-sm btn-dark', onclick: quietly(() => usePalette(palette.name)) }, 'Use')),
                previewBar(palette),
                el('div', { class: 'd-flex flex-wrap gap-1 my-2' },
                    palette.colours.map(c => el('span', { class: 'rounded border', style: `width:22px;height:22px;background:${c}`, title: c }))),
                palette.message ? el('p', { class: 'small text-muted mb-2' }, `“${palette.message}”`) : null,
                el('div', { class: 'd-flex gap-2' },
                    el('button', { type: 'button', class: 'btn btn-sm btn-outline-secondary', onclick: () => startEditing(palette) }, 'Edit'),
                    el('button', { type: 'button', class: 'btn btn-sm btn-outline-danger', disabled: settings.palettes.length <= 1,
                                   onclick: quietly(() => deletePalette(palette.name)) }, 'Delete'))));
    }

    function paletteEditor() {
        const palette = editing.palette;
        const preview = el('div');
        const refreshPreview = () => preview.replaceChildren(previewBar(palette));
        const colourRows = el('div', { class: 'vstack gap-1' });
        const changed = () => { renderColours(); refreshPreview(); };
        const swap = (i, j) => { [palette.colours[i], palette.colours[j]] = [palette.colours[j], palette.colours[i]]; changed(); };
        function renderColours() {
            colourRows.replaceChildren(...palette.colours.map((colour, i) => el('div', { class: 'hstack gap-1' },
                el('input', { type: 'color', class: 'form-control form-control-color', value: colour.toLowerCase(), title: `Colour ${i + 1}`,
                              'aria-label': `Colour ${i + 1}`, oninput: e => { palette.colours[i] = e.target.value.toUpperCase(); refreshPreview(); } }),
                el('span', { class: 'small text-muted me-auto' }, `Colour ${i + 1}`),
                el('button', { type: 'button', class: 'btn btn-sm btn-outline-secondary', disabled: i === 0, title: 'Earlier', 'aria-label': 'Earlier',
                               onclick: () => swap(i, i - 1) }, '↑'),
                el('button', { type: 'button', class: 'btn btn-sm btn-outline-secondary', disabled: i === palette.colours.length - 1, title: 'Later',
                               'aria-label': 'Later', onclick: () => swap(i, i + 1) }, '↓'),
                el('button', { type: 'button', class: 'btn btn-sm btn-outline-danger', disabled: palette.colours.length <= 1, title: 'Remove',
                               'aria-label': 'Remove', onclick: () => { palette.colours.splice(i, 1); changed(); } }, '×'))));
        }
        renderColours();
        refreshPreview();
        const blendValue = el('span', {}, String(Math.round(palette.blend * 100)));
        return el('div', { class: 'card h-100 border-primary border-2' },
            el('div', { class: 'card-body' },
                el('label', { class: 'form-label small mb-1' }, 'Name'),
                el('input', { class: 'form-control form-control-sm', value: palette.name, oninput: e => { palette.name = e.target.value; } }),
                el('label', { class: 'form-label small mt-2 mb-1' }, 'Message for Scrolling Text'),
                el('input', { class: 'form-control form-control-sm', value: palette.message, oninput: e => { palette.message = e.target.value; } }),
                el('div', { class: 'small mt-2 mb-1' }, 'Colours, in the order the animations use them'),
                colourRows,
                el('button', { type: 'button', class: 'btn btn-sm btn-link px-0',
                               onclick: () => { palette.colours.push(palette.colours[palette.colours.length - 1] || '#FFFFFF'); changed(); } }, 'Add a colour'),
                el('label', { class: 'form-label small mt-2 mb-1' }, 'Blend: ', blendValue, '% (0 for solid bands of each colour, 100 for a smooth gradient)'),
                el('input', { type: 'range', class: 'form-range', min: 0, max: 100, value: Math.round(palette.blend * 100),
                              oninput: e => { palette.blend = e.target.value / 100; blendValue.textContent = e.target.value; refreshPreview(); } }),
                preview,
                el('div', { class: 'd-flex gap-2 mt-3' },
                    el('button', { type: 'button', class: 'btn btn-sm btn-dark', onclick: quietly(savePalette) }, 'Save'),
                    el('button', { type: 'button', class: 'btn btn-sm btn-outline-secondary', onclick: () => { editing = null; renderPalettes(); } }, 'Cancel'))));
    }

    function startEditing(palette) {
        editing = { originalName: palette.name, palette: structuredClone(palette) };
        renderPalettes();
    }

    document.getElementById('newPalette').addEventListener('click', () => {
        // Start from a copy of the palette in use
        const inUse = settings.palettes.find(p => p.name === settings.activePalette) || settings.palettes[0];
        const names = new Set(settings.palettes.map(p => p.name.toLowerCase()));
        let name = 'New palette';
        for (let n = 2; names.has(name.toLowerCase()); n++) name = `New palette ${n}`;
        editing = { originalName: null, palette: { ...structuredClone(inUse), name } };
        renderPalettes();
    });

    // (After a palette change the animations' usual colours may have changed too, so they're fetched again)
    async function savePalette() {
        settings = await post('/Configuration/SavePalette', { originalName: editing.originalName, palette: editing.palette });
        editing = null;
        renderPalettes();
        loadAnimationColours();
    }

    async function usePalette(name) {
        settings = await post('/Configuration/UsePalette', { name });
        renderPalettes();
        loadAnimationColours();
    }

    async function deletePalette(name) {
        if (!confirm(`Delete the ${name} palette?`)) return;
        settings = await post('/Configuration/DeletePalette', { name });
        renderPalettes();
        loadAnimationColours();
    }

    // ---- Animation colours

    const saveTimers = {};

    async function loadAnimationColours() {
        try {
            animationColours = await (await fetch('/Configuration/AnimationColours')).json();
            renderAnimationColours();
        } catch (err) {
            showError("Couldn't load the animations' colours: " + err);
        }
    }

    function renderAnimationColours() {
        document.getElementById('animationColours').replaceChildren(
            ...animationColours.map(animation => el('div', { class: 'col-md-6 col-lg-4' }, animationCard(animation))));
    }

    function animationCard(animation) {
        const own = animation.ownColours;
        const fromPalette = animation.roles.some(role => role.kind !== 'Fixed');
        const switchId = `own-${animation.name}`;
        return el('div', { class: 'card h-100' + (own ? ' border-dark border-2' : '') },
            el('div', { class: 'card-body d-flex flex-column' },
                el('div', { class: 'd-flex justify-content-between align-items-center mb-2' },
                    el('h3', { class: 'h6 mb-0' }, animation.label),
                    el('button', { type: 'button', class: 'btn btn-sm btn-outline-dark', onclick: e => showAnimation(animation, e.currentTarget) }, 'Show on tree')),
                el('div', { class: 'form-check form-switch mb-2' },
                    el('input', { class: 'form-check-input', type: 'checkbox', role: 'switch', id: switchId, checked: own,
                                  onchange: e => saveAnimationColours(animation, e.target.checked, null, true) }),
                    el('label', { class: 'form-check-label', for: switchId }, 'Own colours')),
                el('div', { class: 'd-flex flex-wrap gap-2' }, animation.roles.map(role => roleColour(animation, role))),
                el('div', { class: 'small text-muted mt-auto pt-2' },
                    own ? ["Its own colours, whatever palette's in use. ",
                           el('button', { type: 'button', class: 'btn btn-link btn-sm p-0 align-baseline',
                                          onclick: () => saveAnimationColours(animation, true, Object.fromEntries(animation.roles.map(r => [r.name, r.usual])), true) },
                              fromPalette ? "Start again from the palette's colours" : 'Start again from its usual colours')]
                        : fromPalette ? `Its usual colours, from the ${settings.activePalette} palette.` : 'Its usual colours.')));
    }

    // One of an animation's colours: a colour picker if it's using its own colours, otherwise what it usually is (for a
    // colour that goes through the palette, the palette's colours)
    function roleColour(animation, role) {
        const size = 'width:3rem;height:2.25rem';
        let swatch;
        if (animation.ownColours) {
            swatch = el('input', { type: 'color', class: 'form-control form-control-color p-1', style: size, value: (role.own || role.usual).toLowerCase(),
                                   title: role.name, 'aria-label': `${animation.label}: ${role.name}`,
                                   oninput: e => colourPicked(animation, role, e.target.value) });
        } else if (role.kind === 'ThroughPalette') {
            const palette = settings.palettes.find(p => p.name === settings.activePalette) || settings.palettes[0];
            swatch = previewBar(palette);
            swatch.style.cssText += `;${size}`;
            swatch.title = `Goes through the ${palette.name} palette's colours`;
        } else {
            swatch = el('span', { class: 'rounded border d-block', style: `${size};background:${role.usual}`, title: role.usual });
        }
        return el('div', { class: 'd-flex flex-column align-items-center text-center', style: 'width:4.25rem' },
            swatch, el('span', { class: 'small mt-1 lh-sm' }, role.name));
    }

    // While a colour's being picked it's sent (and shown on the tree) as it changes, but not on every movement
    function colourPicked(animation, role, value) {
        role.own = value.toUpperCase();
        clearTimeout(saveTimers[animation.name]);
        saveTimers[animation.name] = setTimeout(() => saveAnimationColours(animation, true, null, false), 150);
    }

    // Saves whether the animation uses its own colours, and its colours (the ones given, or else the ones picked so far).
    // The cards are redrawn afterwards unless a colour's being picked (redrawing would close the colour picker).
    async function saveAnimationColours(animation, ownColours, colours, redraw) {
        clearTimeout(saveTimers[animation.name]);
        const picked = Object.fromEntries(animation.roles.filter(role => role.own).map(role => [role.name, role.own]));
        try {
            const saved = await post('/Configuration/SaveAnimationColours', { name: animation.name, ownColours, colours: colours || picked });
            if (redraw) {
                animationColours = saved;
                renderAnimationColours();
            }
        } catch {
            if (redraw) renderAnimationColours(); // put the switch back
        }
    }

    async function showAnimation(animation, button) {
        try {
            await post('/Configuration/ShowAnimation', { name: animation.name });
        } catch {
            return; // (the error's shown at the top of the page)
        }
        button.textContent = 'On the tree ✓';
        setTimeout(() => { button.textContent = 'Show on tree'; }, 2000);
    }

    // ---- Animation speeds

    const speedContainer = document.getElementById('animationSpeeds');
    const resetAllSpeeds = document.getElementById('resetAllSpeeds');
    const speedTimers = {};

    function animationSpeed(name) {
        const entry = Object.entries(settings.animationSpeeds || {}).find(([k]) => k.toLowerCase() === name.toLowerCase());
        return entry ? entry[1] : 1;
    }

    function formatSpeed(speed) {
        return `${Number(speed.toFixed(2))}×`;
    }

    function isDefaultSpeed(speed) {
        return Math.abs(speed - 1) < 0.005;
    }

    function updateResetAllButton() {
        resetAllSpeeds.disabled = !speedAnimations.some(a => !isDefaultSpeed(animationSpeed(a.name)));
    }

    function renderAnimationSpeeds() {
        const groups = [];
        const byGroup = new Map();
        for (const animation of speedAnimations) {
            if (!byGroup.has(animation.group)) {
                const list = [];
                byGroup.set(animation.group, list);
                groups.push([animation.group, list]);
            }
            byGroup.get(animation.group).push(animation);
        }

        const sections = groups.flatMap(([group, list], index) => [
            el('h3', { class: `h6 text-muted ${index === 0 ? 'mt-0' : 'mt-3'} mb-2` }, group),
            el('div', { class: 'row g-3' }, list.map(animation => el('div', { class: 'col-md-6 col-lg-4' }, speedCard(animation)))),
        ]);
        speedContainer.replaceChildren(...sections);
        updateResetAllButton();
    }

    function speedCard(animation) {
        const speed = animationSpeed(animation.name);
        const custom = !isDefaultSpeed(speed);
        const valueLabel = el('b', {}, formatSpeed(speed));
        const slider = el('input', {
            type: 'range', class: 'form-range', min: speedLimits.min, max: speedLimits.max, step: 0.05, value: speed,
            'aria-label': `${animation.label} speed`,
        });
        const resetButton = el('button', {
            type: 'button', class: 'btn btn-link btn-sm p-0 ms-auto' + (custom ? '' : ' d-none'),
        }, 'Reset');
        const card = el('div', { class: 'card h-100' + (custom ? ' border-dark border-2' : '') },
            el('div', { class: 'card-body d-flex flex-column' },
                el('div', { class: 'd-flex justify-content-between align-items-center gap-2 mb-2' },
                    el('h4', { class: 'h6 mb-0' }, animation.label),
                    el('button', { type: 'button', class: 'btn btn-sm btn-outline-dark flex-shrink-0', onclick: e => showAnimation(animation, e.currentTarget) }, 'Show on tree')),
                el('div', { class: 'd-flex align-items-center gap-2 small mb-1' },
                    el('span', {}, 'Speed: ', valueLabel),
                    resetButton),
                slider));

        function applyVisual(next) {
            const isCustom = !isDefaultSpeed(next);
            valueLabel.textContent = formatSpeed(next);
            resetButton.classList.toggle('d-none', !isCustom);
            card.classList.toggle('border-dark', isCustom);
            card.classList.toggle('border-2', isCustom);
            // Keep settings.animationSpeeds in sync locally so Reset All updates immediately without redrawing sliders
            settings.animationSpeeds = settings.animationSpeeds || {};
            for (const key of Object.keys(settings.animationSpeeds)) {
                if (key.toLowerCase() === animation.name.toLowerCase()) delete settings.animationSpeeds[key];
            }
            if (isCustom) settings.animationSpeeds[animation.name] = next;
            updateResetAllButton();
        }

        async function persistSpeed(next) {
            try {
                settings = await post('/Configuration/SaveAnimationSpeed', { name: animation.name, speed: next });
                updateResetAllButton();
            } catch {
                renderAnimationSpeeds();
            }
        }

        slider.addEventListener('input', e => {
            const next = Number(e.target.value);
            applyVisual(next);
            clearTimeout(speedTimers[animation.name]);
            speedTimers[animation.name] = setTimeout(() => persistSpeed(next), 150);
        });

        resetButton.addEventListener('click', () => {
            clearTimeout(speedTimers[animation.name]);
            slider.value = 1;
            applyVisual(1);
            persistSpeed(1);
        });

        return card;
    }

    resetAllSpeeds.addEventListener('click', quietly(async () => {
        for (const timer of Object.values(speedTimers)) clearTimeout(timer);
        settings = await post('/Configuration/ResetAnimationSpeeds', {});
        renderAnimationSpeeds();
    }));

    // ---- Showcase

    const secondsEach = document.getElementById('secondsEach');
    const showcaseStatus = document.getElementById('showcaseStatus');
    const showcaseBoxes = () => [...document.querySelectorAll('#showcaseEffects input[type=checkbox]')];

    function renderShowcase() {
        document.getElementById('showcaseEffects').replaceChildren(...showcaseEffects.map(effect => el('div', { class: 'col' },
            el('div', { class: 'form-check' },
                el('input', { class: 'form-check-input', type: 'checkbox', id: `showcase-${effect.name}`, value: effect.name,
                              checked: !settings.showcaseExcluded.includes(effect.name) }),
                el('label', { class: 'form-check-label', for: `showcase-${effect.name}` }, effect.label)))));
        secondsEach.value = settings.showcaseSecondsEach;
    }

    function describeSavedShowcase() {
        const count = showcaseEffects.length - settings.showcaseExcluded.filter(n => showcaseEffects.some(e => e.name === n)).length;
        showcaseStatus.textContent = `Saved: ${count} of ${showcaseEffects.length} animations, ${settings.showcaseSecondsEach} seconds each.`;
    }

    document.getElementById('showcaseAll').addEventListener('click', () => showcaseBoxes().forEach(b => { b.checked = true; }));
    document.getElementById('showcaseNone').addEventListener('click', () => showcaseBoxes().forEach(b => { b.checked = false; }));
    document.getElementById('saveShowcase').addEventListener('click', quietly(async () => {
        settings = await post('/Configuration/Showcase', {
            included: showcaseBoxes().filter(b => b.checked).map(b => b.value),
            secondsEach: Number(secondsEach.value) || 30,
        });
        renderShowcase();
        describeSavedShowcase();
    }));

    // ---- Start

    fetch('/Configuration/Settings').then(r => r.json()).then(data => {
        settings = data.settings;
        showcaseEffects = data.showcaseEffects;
        speedAnimations = data.speedAnimations || [];
        if (data.speedLimits) speedLimits = data.speedLimits;
        frontDirection.value = settings.frontDirection;
        frontDirectionValue.textContent = settings.frontDirection;
        mirrored.checked = settings.mirrored;
        describeSavedOrientation();
        leds = data.leds;
        dimmestLevel.value = settings.dimmestLevel;
        switchOffBelow.value = settings.switchOffBelowPercent;
        describeDimLevels();
        dimStatus.textContent = describeLeds();
        renderPalettes();
        renderAnimationSpeeds();
        renderShowcase();
        describeSavedShowcase();
        return loadAnimationColours();
    }).catch(err => showError("Couldn't load the settings: " + err));
})();
