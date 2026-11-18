/* KONGBOX icon set: original line icons on a 24 px grid (1.8 stroke, round joins). Shared by the panel and the editor. */
(function (root) {
  'use strict';
  var I = {
    // navigation & layout
    home: '<path d="M4 10.5 12 4l8 6.5V19a1 1 0 0 1-1 1h-4.5v-5.5h-5V20H5a1 1 0 0 1-1-1z"/>',
    grid: '<rect x="4" y="4" width="7" height="7" rx="2"/><rect x="13" y="4" width="7" height="7" rx="2"/><rect x="4" y="13" width="7" height="7" rx="2"/><rect x="13" y="13" width="7" height="7" rx="2"/>',
    stack: '<rect x="5" y="9" width="14" height="11" rx="2.5"/><path d="M7.5 6h9M10 3h4"/>',
    sliders: '<path d="M5 5v14M12 5v14M19 5v14"/><circle cx="5" cy="15" r="2" fill="currentColor"/><circle cx="12" cy="8" r="2" fill="currentColor"/><circle cx="19" cy="13" r="2" fill="currentColor"/>',
    compass: '<circle cx="12" cy="12" r="8.5"/><path d="m15.5 8.5-2.2 4.8-4.8 2.2 2.2-4.8z"/>',
    search: '<circle cx="10.5" cy="10.5" r="6"/><path d="m15 15 5 5"/>',
    plus: '<path d="M12 5v14M5 12h14"/>',
    minus: '<path d="M5 12h14"/>',
    x: '<path d="m6 6 12 12M18 6 6 18"/>',
    check: '<path d="m5 12.5 4.5 4.5L19 7.5"/>',
    'chevron-right': '<path d="m9.5 5.5 6.5 6.5-6.5 6.5"/>',
    'chevron-left': '<path d="M14.5 5.5 8 12l6.5 6.5"/>',
    arrow: '<path d="M5 12h13M13 6.5l5.5 5.5-5.5 5.5"/>',
    refresh: '<path d="M19 12a7 7 0 1 1-2.05-4.95"/><path d="M19 4.5v4h-4"/>',
    alert: '<path d="M12 4 3.5 19h17z"/><path d="M12 10v4.5"/><circle cx="12" cy="16.9" r=".6" fill="currentColor"/>',
    info: '<circle cx="12" cy="12" r="8.5"/><path d="M12 11v5.5"/><circle cx="12" cy="8" r=".6" fill="currentColor"/>',
    link: '<path d="M10 14a4 4 0 0 0 5.66 0l2.83-2.83a4 4 0 0 0-5.66-5.66L11.5 6.8"/><path d="M14 10a4 4 0 0 0-5.66 0l-2.83 2.83a4 4 0 0 0 5.66 5.66l1.33-1.33"/>',

    // computer
    cpu: '<rect x="6.5" y="6.5" width="11" height="11" rx="2"/><rect x="9.5" y="9.5" width="5" height="5" rx="1"/><path d="M9.5 3.5v3M14.5 3.5v3M9.5 17.5v3M14.5 17.5v3M3.5 9.5h3M3.5 14.5h3M17.5 9.5h3M17.5 14.5h3"/>',
    gpu: '<rect x="3" y="7" width="18" height="10" rx="2"/><circle cx="9" cy="12" r="2.6"/><circle cx="15.5" cy="12" r="2.6"/><path d="M6 17v2.5M10 17v2.5M14 17v2.5"/>',
    memory: '<rect x="3" y="7.5" width="18" height="9" rx="1.5"/><path d="M7 10.5v3M11 10.5v3M15 10.5v3M6 16.5v2.5M10 16.5v2.5M14 16.5v2.5M18 16.5v2.5"/>',
    disk: '<rect x="3.5" y="5" width="17" height="14" rx="2.5"/><path d="M3.5 14h17"/><circle cx="16.5" cy="16.5" r=".7" fill="currentColor"/>',
    network: '<path d="M8 4v15M8 19l-3.5-3.5M8 19l3.5-3.5M16 20V5M16 5l-3.5 3.5M16 5l3.5 3.5"/>',
    signal: '<path d="M5 19v-3M10 19v-6M15 19v-9M20 19V6"/>',
    chart: '<path d="M4 19h16"/><path d="m4.5 15 4.5-5 3.5 3 6.5-7.5"/>',
    gauge: '<path d="M4.5 16.5a8 8 0 1 1 15 0"/><path d="m12 13 3.5-4.5"/><circle cx="12" cy="13.5" r="1.3" fill="currentColor"/>',
    layers: '<path d="m12 4 8.5 4.5L12 13 3.5 8.5z"/><path d="m3.5 12.5 8.5 4.5 8.5-4.5M3.5 16.5 12 21l8.5-4.5"/>',
    terminal: '<rect x="3" y="4.5" width="18" height="15" rx="2.5"/><path d="m7 10 3 2.5L7 15M12.5 15.5H17"/>',
    folder: '<path d="M3.5 7.5a2 2 0 0 1 2-2h4l2 2.5h7a2 2 0 0 1 2 2V17a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z"/>',
    code: '<path d="m8.5 7.5-5 4.5 5 4.5M15.5 7.5l5 4.5-5 4.5M13.5 5l-3 14"/>',
    branch: '<circle cx="7" cy="6" r="2.2"/><circle cx="7" cy="18" r="2.2"/><circle cx="17" cy="8" r="2.2"/><path d="M7 8.2v7.6M17 10.2c0 4-4 4.3-8.3 6.4"/>',
    globe: '<circle cx="12" cy="12" r="8.5"/><path d="M3.5 12h17M12 3.5c2.5 2.6 3.6 5.4 3.6 8.5s-1.1 5.9-3.6 8.5c-2.5-2.6-3.6-5.4-3.6-8.5S9.5 6.1 12 3.5z"/>',
    monitor: '<rect x="3" y="4.5" width="18" height="12" rx="2"/><path d="M9 20h6M12 16.5V20"/>',
    keyboard: '<rect x="2.5" y="6" width="19" height="12" rx="2.5"/><path d="M6 10h.01M9.5 10h.01M13 10h.01M16.5 10h.01M6 13.5h.01M18 13.5h.01M9 14h6" stroke-width="2.2"/>',
    calculator: '<rect x="5" y="3" width="14" height="18" rx="2.5"/><rect x="8" y="6" width="8" height="3.5" rx="1"/><path d="M8.5 13h.01M12 13h.01M15.5 13h.01M8.5 16.5h.01M12 16.5h.01M15.5 16.5h.01" stroke-width="2.4"/>',
    gear: '<circle cx="12" cy="12" r="3"/><path d="M12 3.5v2.2M12 18.3v2.2M20.5 12h-2.2M5.7 12H3.5M18 6l-1.6 1.6M7.6 16.4 6 18M18 18l-1.6-1.6M7.6 7.6 6 6"/><circle cx="12" cy="12" r="6.3"/>',
    scissors: '<circle cx="6.5" cy="7" r="2.5"/><circle cx="6.5" cy="17" r="2.5"/><path d="M8.6 8.4 20 17M8.6 15.6 20 7"/>',
    activity: '<path d="M3 12h4l2.5-6 5 12 2.5-6h4"/>',
    power: '<path d="M12 3.5v8"/><path d="M7 6.5a7.5 7.5 0 1 0 10 0"/>',
    lock: '<rect x="5" y="10.5" width="14" height="10" rx="2.5"/><path d="M8.5 10.5V8a3.5 3.5 0 0 1 7 0v2.5"/>',
    unlock: '<rect x="5" y="10.5" width="14" height="10" rx="2.5"/><path d="M8.5 10.5V8a3.5 3.5 0 0 1 6.8-1.2"/>',
    shield: '<path d="M12 3.5 19 6v5.5c0 4.5-3 7.6-7 9-4-1.4-7-4.5-7-9V6z"/><path d="m9 12 2.2 2.2L15.5 10"/>',
    bolt: '<path d="M13.5 3 5.5 13.5H12L10.5 21l8-10.5H12z"/>',
    usb: '<path d="M12 3v14"/><path d="m9 6 3-3 3 3"/><path d="M12 12.5 7.5 10V7.5M12 14.5l4.5-2.5V9.5"/><rect x="6.5" y="6" width="2" height="2" rx=".4" fill="currentColor"/><circle cx="16.5" cy="8.5" r="1" fill="currentColor"/><circle cx="12" cy="19" r="2"/>',
    wifi: '<path d="M3 9.5a13 13 0 0 1 18 0M6 13a8.5 8.5 0 0 1 12 0M9 16.5a4 4 0 0 1 6 0"/><circle cx="12" cy="19.3" r=".9" fill="currentColor"/>',
    rocket: '<path d="M12 3.5c3.5 2 5 5.5 5 9.5l-2 3.5H9L7 13c0-4 1.5-7.5 5-9.5z"/><circle cx="12" cy="10" r="1.8"/><path d="M9.5 19.5 12 21l2.5-1.5M7 13l-2.5 2.5V18L8 17M17 13l2.5 2.5V18L16 17"/>',

    // media & sound
    music: '<path d="M9 17.5V5.5l10-2v12"/><circle cx="6.5" cy="17.5" r="2.5"/><circle cx="16.5" cy="15.5" r="2.5"/>',
    headphones: '<path d="M4 15v-3a8 8 0 0 1 16 0v3"/><rect x="3.5" y="14" width="4" height="6.5" rx="1.6"/><rect x="16.5" y="14" width="4" height="6.5" rx="1.6"/>',
    play: '<path d="M8 5.5v13l10.5-6.5z" fill="currentColor"/>',
    pause: '<rect x="6.5" y="5" width="3.6" height="14" rx="1" fill="currentColor"/><rect x="13.9" y="5" width="3.6" height="14" rx="1" fill="currentColor"/>',
    next: '<path d="M5.5 6v12l8.5-6z" fill="currentColor"/><path d="M17.5 6v12"/>',
    prev: '<path d="M18.5 6v12L10 12z" fill="currentColor"/><path d="M6.5 6v12"/>',
    volume: '<path d="M4 9.5h3.5L12 5.5v13l-4.5-4H4z" fill="currentColor"/><path d="M15.5 9.5a3.5 3.5 0 0 1 0 5M18 7a7 7 0 0 1 0 10"/>',
    mute: '<path d="M4 9.5h3.5L12 5.5v13l-4.5-4H4z" fill="currentColor"/><path d="m16 9.5 5 5M21 9.5l-5 5"/>',
    mic: '<rect x="9" y="3.5" width="6" height="11" rx="3"/><path d="M5.5 11.5a6.5 6.5 0 0 0 13 0M12 18v2.5"/>',
    radio: '<path d="M7.5 7.5a6.4 6.4 0 0 0 0 9M16.5 7.5a6.4 6.4 0 0 1 0 9M4.6 4.6a10.5 10.5 0 0 0 0 14.8M19.4 4.6a10.5 10.5 0 0 1 0 14.8"/><circle cx="12" cy="12" r="1.8" fill="currentColor"/>',

    // time
    clock: '<circle cx="12" cy="12" r="8.5"/><path d="M12 7.5V12l3 2"/>',
    alarm: '<circle cx="12" cy="13" r="7"/><path d="M12 9.5V13l2.5 1.5M4 6l3-2.5M20 6l-3-2.5"/>',
    timer: '<circle cx="12" cy="13.5" r="7"/><path d="M10 3h4M12 3v3.5M12 13.5l3-3M18.5 7l1.5-1.5"/>',
    stopwatch: '<circle cx="12" cy="13.5" r="7"/><path d="M10 3h4M12 3v3.5"/><path d="M12 13.5V10" stroke-width="2.2"/>',
    calendar: '<rect x="3.5" y="5" width="17" height="15.5" rx="2.5"/><path d="M3.5 9.5h17M8 3v4M16 3v4"/><circle cx="8.5" cy="14" r=".9" fill="currentColor"/><circle cx="12" cy="14" r=".9" fill="currentColor"/>',
    hourglass: '<path d="M6.5 3.5h11M6.5 20.5h11M7.5 3.5c0 5 9 5 9 8.5s-9 3.5-9 8.5M16.5 3.5c0 5-9 5-9 8.5s9 3.5 9 8.5"/>',
    target: '<circle cx="12" cy="12" r="8.5"/><circle cx="12" cy="12" r="5"/><circle cx="12" cy="12" r="1.5" fill="currentColor"/>',
    world: '<circle cx="12" cy="12" r="8.5"/><path d="M5 8.5c2 .5 3 2 4.5 1.5s1-3 3-3.5M13 20.3c-.5-2.3 1-3 2.5-4.3s3.5-.5 4.8-1.8M5.5 16c1.5-1 3 0 4 1.5"/>',
    letters: '<path d="M4 18 8 6l4 12M5.3 14h5.4M14.5 9.5h4a2 2 0 0 1 0 4h-4V6h3.5a1.8 1.8 0 0 1 0 3.5M14.5 13.5h4.3a2.25 2.25 0 0 1 0 4.5h-4.3z"/>',

    // weather & sky
    sun: '<circle cx="12" cy="12" r="4"/><path d="M12 2.8v2.4M12 18.8v2.4M21.2 12h-2.4M5.2 12H2.8M18.5 5.5l-1.7 1.7M7.2 16.8l-1.7 1.7M18.5 18.5l-1.7-1.7M7.2 7.2 5.5 5.5"/>',
    moon: '<path d="M19.5 14.5A8 8 0 0 1 9.5 4.5a8 8 0 1 0 10 10z"/>',
    cloud: '<path d="M7 18.5h10.5a4 4 0 0 0 .4-8 6 6 0 0 0-11.6 1.6A3.3 3.3 0 0 0 7 18.5z"/>',
    rain: '<path d="M7 15h10.5a4 4 0 0 0 .4-8 6 6 0 0 0-11.6 1.6A3.3 3.3 0 0 0 7 15z"/><path d="m8.5 18-1 2.5M12.5 18l-1 2.5M16.5 18l-1 2.5"/>',
    sunrise: '<path d="M3 18h18M6.5 14.5a5.5 5.5 0 0 1 11 0M12 3.5v4M9.5 5.5 12 3l2.5 2.5M4 11l1.5 1M20 11l-1.5 1"/>',
    leaf: '<path d="M5 19c0-8 5-13 14-14 0 9-5 14-13 14z"/><path d="M5 19 13 11"/>',
    wind: '<path d="M3.5 9h11a2.5 2.5 0 1 0-2.5-2.5M3.5 13h15a2.5 2.5 0 1 1-2.5 2.5M3.5 17h6"/>',
    thermo: '<path d="M10 14.5V5a2 2 0 0 1 4 0v9.5a4 4 0 1 1-4 0z"/><path d="M12 17.5v-6"/>',

    // ai & code
    sparkle: '<path d="M12 3.5c.6 4.4 2.6 6.9 7.5 8.5-4.9 1.6-6.9 4.1-7.5 8.5-.6-4.4-2.6-6.9-7.5-8.5 4.9-1.6 6.9-4.1 7.5-8.5z"/>',
    robot: '<rect x="4.5" y="8" width="15" height="11" rx="3"/><path d="M12 8V4.5"/><circle cx="12" cy="3.8" r="1" fill="currentColor"/><circle cx="9" cy="13" r="1.2" fill="currentColor"/><circle cx="15" cy="13" r="1.2" fill="currentColor"/><path d="M9.5 16.3h5M2.5 12.5v2.5M21.5 12.5v2.5"/>',
    ring: '<circle cx="12" cy="12" r="8" opacity=".3"/><path d="M12 4a8 8 0 1 1-7.4 5"/>',
    brain: '<path d="M9 4.5A3 3 0 0 0 6 7.5a3 3 0 0 0-2 5 3 3 0 0 0 2 5 3 3 0 0 0 6 .5V6a1.8 1.8 0 0 0-3-1.5zM15 4.5a3 3 0 0 1 3 3 3 3 0 0 1 2 5 3 3 0 0 1-2 5 3 3 0 0 1-6 .5"/>',

    // personal & data
    note: '<path d="M6 3.5h8.5L19 8v11.5a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1v-15a1 1 0 0 1 1-1z"/><path d="M14 3.5V8h5M8.5 12.5h7M8.5 16h5"/>',
    pencil: '<path d="m15.5 4.5 4 4L9 19H5v-4z"/><path d="m13.5 6.5 4 4"/>',
    checklist: '<path d="m4 6.5 1.6 1.6L8.5 5M4 13.5l1.6 1.6 2.9-3.1M11.5 7h8.5M11.5 14h8.5M11.5 19h6"/>',
    counter: '<rect x="3.5" y="5" width="17" height="14" rx="3"/><path d="M8 12h3M9.5 10.5v3M14 12h3"/>',
    breathe: '<circle cx="12" cy="12" r="3"/><circle cx="12" cy="12" r="6" opacity=".55"/><circle cx="12" cy="12" r="9" opacity=".25"/>',
    quote: '<path d="M10 8.5H6.5a1 1 0 0 0-1 1V13a1 1 0 0 0 1 1H10v1.5c0 1.5-1 2.5-3 3M18.5 8.5H15a1 1 0 0 0-1 1V13a1 1 0 0 0 1 1h3.5v1.5c0 1.5-1 2.5-3 3"/>',
    tag: '<path d="M3.5 11.6V4.5a1 1 0 0 1 1-1h7.1l8.9 8.9-8.1 8.1z"/><circle cx="8" cy="8" r="1.4" fill="currentColor"/>',
    battery: '<rect x="2.5" y="7.5" width="17" height="9" rx="2.2"/><path d="M21.5 10.5v3"/><rect x="4.8" y="9.8" width="9" height="4.4" rx=".8" fill="currentColor" stroke="none"/>',
    bell: '<path d="M6 16V11a6 6 0 0 1 12 0v5l1.5 2h-15z"/><path d="M10 20.5a2.3 2.3 0 0 0 4 0"/>',
    inbox: '<path d="M3.5 13.5 6 5.5h12l2.5 8V18a1.5 1.5 0 0 1-1.5 1.5H5A1.5 1.5 0 0 1 3.5 18z"/><path d="M3.5 13.5h5l1 2h5l1-2h5"/>',
    news: '<rect x="3.5" y="4.5" width="17" height="15" rx="2"/><path d="M7 8.5h10M7 12h4.5M7 15.5h4.5"/><rect x="13.5" y="11.5" width="3.5" height="4" rx=".6"/>',
    exchange: '<path d="M4.5 8.5h13l-3.5-3.5M19.5 15.5h-13l3.5 3.5"/>',
    coin: '<ellipse cx="12" cy="12" rx="8.5" ry="8.5"/><path d="M14.5 9.3c-.4-1-1.4-1.6-2.6-1.6-1.6 0-2.6.8-2.6 2s1.1 1.7 2.6 2c1.6.3 2.7 1 2.7 2.2s-1.1 2.1-2.7 2.1c-1.3 0-2.4-.7-2.8-1.7M12 6v1.7M12 16v1.8"/>',
    image: '<rect x="3.5" y="4.5" width="17" height="15" rx="2.5"/><circle cx="9" cy="9.5" r="1.7"/><path d="m3.5 17 5-5 4 4 2.5-2.5 5.5 5.5"/>',
    chat: '<path d="M5 18.5 3.5 21l.3-3.6A8 8 0 1 1 7 19.5z"/>',
    mail: '<rect x="3" y="5.5" width="18" height="13" rx="2.5"/><path d="m4 7 8 6 8-6"/>',
    heart: '<path d="M12 20s-7.5-4.4-7.5-10A4.3 4.3 0 0 1 12 7.5 4.3 4.3 0 0 1 19.5 10c0 5.6-7.5 10-7.5 10z"/>',
    star: '<path d="m12 3.8 2.5 5.2 5.7.8-4.1 4 1 5.7L12 16.8l-5.1 2.7 1-5.7-4.1-4 5.7-.8z"/>',
    bed: '<path d="M3 18.5V6M3 14h18v4.5M21 14v-2.5a3 3 0 0 0-3-3h-7V14"/><circle cx="7" cy="10.5" r="1.8"/>',
    coffee: '<path d="M4.5 9h12v5.5a5 5 0 0 1-5 5h-2a5 5 0 0 1-5-5zM16.5 10.5h1.5a2.5 2.5 0 0 1 0 5h-1.8M8 3.5v2.5M11.5 3.5v2.5"/>',
    flame: '<path d="M12 21a6.5 6.5 0 0 0 6.5-6.5c0-4.5-4-6.5-4.5-11-3 2-5.5 5.5-5 9-1.3-.5-2-1.8-2.2-3C5.3 11.2 5.5 13 5.5 14.5A6.5 6.5 0 0 0 12 21z"/>',
    palette: '<path d="M12 3.5a8.5 8.5 0 1 0 0 17c1.4 0 2-1 1.4-2.2-.7-1.4.2-2.8 1.8-2.8H17a3.5 3.5 0 0 0 3.5-3.5c0-4.7-3.8-8.5-8.5-8.5z"/><circle cx="7.8" cy="12" r="1.1" fill="currentColor"/><circle cx="10" cy="7.8" r="1.1" fill="currentColor"/><circle cx="14.6" cy="7.8" r="1.1" fill="currentColor"/>',
    webhook: '<circle cx="7" cy="16" r="2.2"/><circle cx="17" cy="16" r="2.2"/><circle cx="12" cy="7" r="2.2"/><path d="M11 9 7.8 14M13 9l3.2 5M9.2 16h5.6"/>',
    phone: '<rect x="6.5" y="2.5" width="11" height="19" rx="2.5"/><path d="M10.5 18.5h3"/>',
    braces: '<path d="M8.5 4.5H8a2 2 0 0 0-2 2V10a2 2 0 0 1-2 2 2 2 0 0 1 2 2v3.5a2 2 0 0 0 2 2h.5M15.5 4.5h.5a2 2 0 0 1 2 2V10a2 2 0 0 0 2 2 2 2 0 0 0-2 2v3.5a2 2 0 0 1-2 2h-.5"/>',
    store: '<path d="M4 9.5 5.5 4.5h13L20 9.5M4 9.5v10h16v-10M4 9.5a2.7 2.7 0 0 0 5.3 0 2.7 2.7 0 0 0 5.4 0 2.7 2.7 0 0 0 5.3 0M10 19.5v-5h4v5"/>',
  };

  // shortcuts and pages saved before the icon set (or typed by hand) may hold an emoji: map the common ones
  var EMOJI = {
    '🏠': 'home', '✳️': 'sparkle', '🤖': 'robot', '📊': 'chart', '⚙️': 'gear', '🎛️': 'sliders', '🎵': 'music', '🍅': 'target',
    '🌍': 'world', '🕰️': 'clock', '🧭': 'compass', '🟢': 'activity', '🌿': 'leaf', '📈': 'chart', '📡': 'radio', '📋': 'checklist',
    '🛏️': 'bed', '🧠': 'brain', '🫧': 'breathe', '🗂️': 'stack', '⌨️': 'terminal', '📁': 'folder', '📝': 'code', '🐙': 'branch',
    '🎧': 'headphones', '🧮': 'calculator', '✂️': 'scissors', '🔇': 'mute', '🌙': 'moon', '🔒': 'lock', '⚡': 'bolt', '▢': 'grid',
    '🌐': 'globe', '🔊': 'volume', '🎮': 'gpu', '🔥': 'flame', '💽': 'disk', '📟': 'gauge', '🔘': 'target', '🧩': 'bolt',
  };

  function svg(inner, cls) {
    return '<svg class="' + (cls || 'ic') + '" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + inner + '</svg>';
  }

  var api = {
    names: Object.keys(I),
    has: function (n) { return Object.prototype.hasOwnProperty.call(I, n); },
    /** An icon name → SVG; an emoji we know → its icon; anything else is shown as text (legacy values). */
    html: function (value, cls) {
      var v = String(value == null ? '' : value).trim();
      if (api.has(v)) return svg(I[v], cls);
      if (EMOJI[v]) return svg(I[EMOJI[v]], cls);
      if (!v) return svg(I.grid, cls);
      var esc = v.replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; });
      return '<span class="' + (cls || 'ic') + ' ic-text">' + esc + '</span>';
    },
  };
  root.KBIcons = api;
})(window);
