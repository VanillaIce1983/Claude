// Sheet metal parameters and the bend math (bend allowance, bend deduction,
// K-factor, setbacks). All lengths in mm, angles in radians.

export type BendMethod = 'k' | 'ba' | 'bd';
export type ReliefType = 'rect' | 'obround' | 'tear';

export interface BendCalc {
  method: BendMethod;
  /** Position of the neutral fibre as a fraction of the thickness (0…1). */
  kFactor: number;
  /** Fixed bend allowance in mm (method 'ba'). */
  ba: number;
  /** Fixed bend deduction in mm (method 'bd'). */
  bd: number;
}

export interface ReliefSettings {
  type: ReliefType;
  /** Relief width and overrun past the bend region as a multiple of the thickness. */
  ratio: number;
}

export interface Material {
  name: string;
  /** Density in g/cm³. */
  density: number;
}

export interface SheetSettings {
  thickness: number;
  radius: number;
  calc: BendCalc;
  relief: ReliefSettings;
  material: Material;
}

export const MATERIALS: Material[] = [
  { name: 'Stahl (DC01)', density: 7.85 },
  { name: 'Edelstahl (1.4301)', density: 7.9 },
  { name: 'Aluminium (AlMg3)', density: 2.66 },
  { name: 'Kupfer', density: 8.94 },
  { name: 'Messing', density: 8.5 },
];

export function defaultSheet(): SheetSettings {
  return {
    thickness: 2,
    radius: 2,
    calc: { method: 'k', kFactor: 0.5, ba: 3, bd: 3 },
    relief: { type: 'rect', ratio: 0.5 },
    material: { ...MATERIALS[0] },
  };
}

/** Per-feature overrides ("Benutzerdefinierte Biegezugabe", own radius, own relief). */
export interface BendOverrides {
  radius?: number;
  calc?: BendCalc;
  relief?: ReliefSettings;
}

export interface ResolvedBendParams {
  T: number;
  R: number;
  calc: BendCalc;
  relief: ReliefSettings;
}

export function resolveBendParams(sheet: SheetSettings, o?: BendOverrides): ResolvedBendParams {
  return {
    T: sheet.thickness,
    R: o?.radius ?? sheet.radius,
    calc: o?.calc ?? sheet.calc,
    relief: o?.relief ?? sheet.relief,
  };
}

/** Distance from the tangent line to the outer virtual sharp: OSSB = (R + T)·tan(θ/2). */
export function outsideSetback(angle: number, R: number, T: number): number {
  return (R + T) * Math.tan(angle / 2);
}

/** Distance from the tangent line to the inner virtual sharp: ISSB = R·tan(θ/2). */
export function insideSetback(angle: number, R: number): number {
  return R * Math.tan(angle / 2);
}

/**
 * Developed length of the bend zone (length of the neutral fibre).
 * - K-factor:        BA = θ·(R + K·T)
 * - Bend allowance:  BA as given
 * - Bend deduction:  BA = 2·OSSB − BD
 */
export function bendAllowance(angle: number, R: number, T: number, calc: BendCalc): number {
  let ba: number;
  switch (calc.method) {
    case 'k':
      if (!(calc.kFactor >= 0 && calc.kFactor <= 1)) throw new Error('Der K-Faktor muss zwischen 0 und 1 liegen.');
      ba = angle * (R + calc.kFactor * T);
      break;
    case 'ba':
      ba = calc.ba;
      break;
    case 'bd':
      if (angle >= Math.PI - 1e-9) {
        throw new Error('Ein Biegeabzug ist für Biegungen ab 180° nicht definiert. Bitte K-Faktor oder Biegezugabe verwenden.');
      }
      ba = 2 * outsideSetback(angle, R, T) - calc.bd;
      break;
  }
  if (!(ba >= 0)) throw new Error('Die berechnete Biegezugabe ist negativ. Bitte Biegeabzug oder Radius prüfen.');
  return ba;
}

/** Bend deduction for display: BD = 2·OSSB − BA. */
export function bendDeduction(angle: number, R: number, T: number, ba: number): number {
  return 2 * outsideSetback(angle, R, T) - ba;
}

export function describeCalc(calc: BendCalc): string {
  switch (calc.method) {
    case 'k':
      return `K-Faktor ${calc.kFactor}`;
    case 'ba':
      return `Biegezugabe ${calc.ba} mm`;
    case 'bd':
      return `Biegeabzug ${calc.bd} mm`;
  }
}

export function validateSheet(s: SheetSettings): void {
  if (!(s.thickness > 0)) throw new Error('Die Blechdicke muss größer als 0 sein.');
  if (!(s.radius >= 0)) throw new Error('Der Biegeradius darf nicht negativ sein.');
  if (!(s.relief.ratio >= 0.05 && s.relief.ratio <= 2)) throw new Error('Das Freistichverhältnis muss zwischen 0,05 und 2 liegen.');
  if (!(s.material.density > 0)) throw new Error('Die Dichte muss größer als 0 sein.');
}
