// Line icons drawn for UnsolidWorks (24 × 24, currentColor).

const svg = (body: string, vb = '0 0 24 24'): string =>
  `<svg viewBox="${vb}" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">${body}</svg>`;

const fill = 'fill="currentColor" fill-opacity=".14"';

export const ICONS = {
  logo: `<svg viewBox="0 0 32 32" fill="none" stroke-linejoin="round" stroke-linecap="round">
    <path d="M5 9h15v15H5z" fill="var(--accent)" fill-opacity=".18" stroke="var(--accent)" stroke-width="2"/>
    <path d="M20 9l6-4v15l-6 4" fill="var(--accent)" fill-opacity=".45" stroke="var(--accent)" stroke-width="2"/>
    <path d="M5 9l6-4h15" stroke="var(--accent)" stroke-width="2"/>
    <path d="M9 14h7M9 18h4" stroke="var(--fg)" stroke-width="1.6" stroke-dasharray="2 2"/></svg>`,
  sheet: svg(`<path d="M3 15l9-5 9 5-9 5z" ${fill}/><path d="M3 15v2l9 5 9-5v-2"/><path d="M12 3v4M10 5l2 2 2-2"/>`),
  baseFlange: svg(`<path d="M3 13l9-5 9 5-9 5z" ${fill}/><path d="M3 13v2l9 5 9-5v-2"/>`),
  edgeFlange: svg(`<path d="M3 15l9-5 6 3.3-9 5z" ${fill}/><path d="M18 13.3V5l-9 5v8.3" ${fill}/><path d="M3 15v1.6l6 3.4"/>`),
  hem: svg(`<path d="M3 16h13a3 3 0 0 0 0-6H8"/><path d="M3 18.4h13a5.4 5.4 0 0 0 0-10.8H8" /><path d="M8 7.6v2.4"/>`),
  sketchedBend: svg(`<path d="M3 16l7-4 7 4-7 4z" ${fill}/><path d="M10 12l5-8 7 4-5 8" ${fill}/><path d="M10 12l7 4" stroke-dasharray="2 2"/>`),
  jog: svg(`<path d="M2 18h7l4-8h9"/><path d="M2 20.4h8.4l4-8H22" /><path d="M2 18v2.4M22 10v2.4"/>`),
  cut: svg(`<path d="M3 14l9-5 9 5-9 5z" ${fill}/><path d="M3 14v2l9 5 9-5v-2"/><ellipse cx="12" cy="14" rx="3" ry="1.6"/>`),
  tab: svg(`<path d="M3 15l9-5 9 5-9 5z" ${fill}/><path d="M16 12.8l4-2.2 3 1.7-4 2.2" ${fill}/><path d="M3 15v1.6l9 5 9-5"/>`),
  closedCorner: svg(`<path d="M4 18V8l8-4v10z" ${fill}/><path d="M12 14V4l8 4v10z" ${fill}/><path d="M4 18l8-4 8 4" stroke-dasharray="2 2"/>`),
  breakCorner: svg(`<path d="M4 20V9a5 5 0 0 1 5-5h11" ${fill}/><path d="M4 20h16V4"/>`),
  flat: svg(`<path d="M8 3h8v5h5v8h-5v5H8v-5H3V8h5z" ${fill}/><path d="M8 8h8v8H8z" stroke-dasharray="2 2"/>`),
  newDoc: svg(`<path d="M6 3h8l4 4v14H6z"/><path d="M14 3v4h4M12 11v6M9 14h6"/>`),
  open: svg(`<path d="M3 7h6l2 2h10v10H3z"/><path d="M3 11h18"/>`),
  save: svg(`<path d="M12 4v11M8 11l4 4 4-4"/><path d="M4 15v5h16v-5"/>`),
  exportIcon: svg(`<path d="M12 15V4M8 8l4-4 4 4"/><path d="M5 12v8h14v-8"/>`),
  undo: svg(`<path d="M9 7L4 12l5 5"/><path d="M4 12h10a6 6 0 0 1 0 12h-2" transform="translate(0 -5)"/>`),
  redo: svg(`<path d="M15 7l5 5-5 5"/><path d="M20 12H10a6 6 0 0 0 0 12h2" transform="translate(0 -5)"/>`),
  check: svg(`<path d="M5 12.5l4.5 4.5L19 7.5" stroke-width="2.2"/>`),
  close: svg(`<path d="M6 6l12 12M18 6L6 18" stroke-width="2"/>`),
  trash: svg(`<path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3"/>`),
  eyeOff: svg(`<path d="M3 12s3.5-6 9-6 9 6 9 6-3.5 6-9 6-9-6-9-6z"/><path d="M4 4l16 16"/>`),
  eye: svg(`<path d="M3 12s3.5-6 9-6 9 6 9 6-3.5 6-9 6-9-6-9-6z"/><circle cx="12" cy="12" r="2.5"/>`),
  edit: svg(`<path d="M4 20h4L19 9l-4-4L4 16z"/><path d="M13 7l4 4"/>`),
  fit: svg(`<path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5"/>`),
  cube: svg(`<path d="M12 3l8 4.5v9L12 21l-8-4.5v-9z"/><path d="M12 12l8-4.5M12 12v9M12 12L4 7.5"/>`),
  plus: svg(`<path d="M12 5v14M5 12h14"/>`),
  sketch: svg(`<path d="M4 18l5-11 5 7 6-9"/><circle cx="4" cy="18" r="1.4"/><circle cx="9" cy="7" r="1.4"/><circle cx="14" cy="14" r="1.4"/><circle cx="20" cy="5" r="1.4"/>`),
  rect: svg(`<rect x="4" y="6" width="16" height="12"/>`),
  pointer: svg(`<path d="M6 3l12 8-5 1.5L16 20l-2.5 1-3-7.5L6 17z"/>`),
  up: svg(`<path d="M12 19V5M6 11l6-6 6 6"/>`),
  down: svg(`<path d="M12 5v14M6 13l6 6 6-6"/>`),
};

export type IconName = keyof typeof ICONS;

/** Small profile diagrams for the flange position choices (side view, base horizontal). */
export const POSITION_DIAGRAMS: Record<string, string> = {
  materialInside: `<svg viewBox="0 0 34 26"><path d="M17 1v24" stroke="var(--sel)" stroke-dasharray="2 2"/><path d="M2 20h11a4 4 0 0 0 4-4V3h-3v13a1 1 0 0 1-1 1H2z" fill="currentColor" fill-opacity=".25" stroke="currentColor"/></svg>`,
  materialOutside: `<svg viewBox="0 0 34 26"><path d="M17 1v24" stroke="var(--sel)" stroke-dasharray="2 2"/><path d="M2 20h14a4 4 0 0 0 4-4V3h-3v13a1 1 0 0 1-1 1H2z" fill="currentColor" fill-opacity=".25" stroke="currentColor"/></svg>`,
  bendOutside: `<svg viewBox="0 0 34 26"><path d="M17 1v24" stroke="var(--sel)" stroke-dasharray="2 2"/><path d="M2 20h15a5 5 0 0 0 5-5V3h-3v12a2 2 0 0 1-2 2H2z" fill="currentColor" fill-opacity=".25" stroke="currentColor"/></svg>`,
  virtualSharp: `<svg viewBox="0 0 34 26"><path d="M17 1v24" stroke="var(--sel)" stroke-dasharray="2 2"/><path d="M2 20h11a4 4 0 0 0 4-4V3h-3v13a1 1 0 0 1-1 1H2z" fill="currentColor" fill-opacity=".25" stroke="currentColor"/><circle cx="17" cy="20" r="1.6" fill="var(--accent)" stroke="none"/></svg>`,
  tangentToBend: `<svg viewBox="0 0 34 26"><path d="M17 1v24" stroke="var(--sel)" stroke-dasharray="2 2"/><path d="M2 20h8a7 7 0 0 0 7-7c0-3-2-6-5-8l-1.5 2.4C12.6 9 14 11 14 13a4 4 0 0 1-4 4H2z" fill="currentColor" fill-opacity=".25" stroke="currentColor"/></svg>`,
  centerline: `<svg viewBox="0 0 34 26"><path d="M14 1v24" stroke="var(--sel)" stroke-dasharray="2 2"/><path d="M2 20h8a4 4 0 0 0 4-4V3h-3v13a1 1 0 0 1-1 1H2z" fill="currentColor" fill-opacity=".25" stroke="currentColor"/></svg>`,
};
