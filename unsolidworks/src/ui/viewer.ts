// three.js viewport: shaded part with crisp edges, edge/face/vertex picking,
// selection highlights, measure dimensions, flat-pattern bend lines and
// standard views.

import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import { LineMaterial } from 'three/examples/jsm/lines/LineMaterial.js';
import { LineSegments2 } from 'three/examples/jsm/lines/LineSegments2.js';
import { LineSegmentsGeometry } from 'three/examples/jsm/lines/LineSegmentsGeometry.js';
import type { FlatPattern } from '../kernel/flat';
import type { MeasureLine } from '../kernel/measure';
import type { PartMesh, PickEdge, Surface } from '../kernel/mesh';
import type { CircleInfo, Part } from '../kernel/model';
import { Affine, V2, V3, applyA, applyDirA, cross2, dot2, invertRigid, sub2, v3 } from '../kernel/vec';

/**
 * 'select': faces and bends are clickable to select their feature (no command running).
 * 'face': flat faces only, for commands that place something on a face.
 * 'none': nothing is clickable, so nothing lights up under the cursor.
 */
export type PickMode = 'none' | 'select' | 'edge' | 'face' | 'measure';

export interface VertexHit {
  kind: 'vertex';
  p: V3;
}

export interface EdgeHit {
  kind: 'edge';
  edge: PickEdge;
  side: 'top' | 'bottom';
}

export interface FaceHit {
  kind: 'face';
  pieceKind: 'face' | 'bend';
  id: string;
  feature: string;
  /** Top (0), bottom (1) or side wall (2) of the sheet. */
  surface: Surface;
  /** Hit point in world space. */
  point: V3;
  /** Flat coordinates of the hit point (faces only). */
  flat?: { x: number; y: number };
}

export type Hit = VertexHit | EdgeHit | FaceHit;

/** A measure pick as the viewer draws it, independent of the feature selection. */
export type MeasureSelectionView =
  | { kind: 'vertex'; p: V3 }
  | { kind: 'edge'; edge: PickEdge; side: 'top' | 'bottom' }
  | { kind: 'face'; pieceKind: 'face' | 'bend'; id: string };

const EDGE_PX = 11;
const MEASURE_EDGE_PX = 10;
const VERTEX_PX = 8;
/**
 * Inside a narrow piece (a bend strip, a hem) the measure snap radius shrinks
 * to this share of the piece's width on screen, so the middle of the piece
 * stays pickable instead of always snapping to one of its border edges.
 */
const NARROW_SNAP_SHARE = 0.3;
/** Smallest snap radius inside a narrow piece: an edge still wins when the cursor is right on it. */
const MIN_SNAP_PX = 1.5;
/** Below this turn, a vertex between two edges of one tag is a tessellated arc point, not a corner. */
const SMOOTH_TURN = (20 * Math.PI) / 180;
/** Measure overlays draw after the part, whatever their distance. */
const OVERLAY_ORDER = 10;

export interface ViewColors {
  steel: string;
  edge: string;
  sel: string;
  accent: string;
  up: string;
  down: string;
}

export type ViewName = 'iso' | 'top' | 'front' | 'right' | 'normal';

/**
 * Raw pointer input while sketching: the left button (one finger on touch)
 * goes to the sketch instead of rotating the view or picking.
 */
export interface SketchPointerHandler {
  down(x: number, y: number, ev: PointerEvent): void;
  move(x: number, y: number, ev: PointerEvent): void;
  up(x: number, y: number, ev: PointerEvent): void;
  /** The pointer left the viewport. */
  leave(): void;
  /** A second finger turned the gesture into a pan or zoom: drop what the first one started. */
  cancel(): void;
}

export interface OverlayLines {
  /** Segment pairs, flat [ax, ay, az, bx, by, bz, …] in world space. */
  seg: number[];
  color: string;
  width: number;
  dashed?: boolean;
  /** Drawn over the part (dimensions, highlights); otherwise hidden behind it like its edges. */
  onTop?: boolean;
}

/** What a sketch draws into the viewport; rebuilt as a whole by setSketchOverlay. */
export interface SketchOverlay {
  lines: OverlayLines[];
  points: { pts: V3[]; color: string; size: number }[];
  /** HTML elements kept at world points; they may be interactive. */
  labels: { el: HTMLElement; at: V3 }[];
}

interface ModelState {
  mesh: PartMesh;
  part: Part;
  flat: boolean;
}

export class Viewer {
  readonly renderer: THREE.WebGLRenderer;
  readonly scene = new THREE.Scene();
  readonly camera: THREE.OrthographicCamera;
  readonly controls: OrbitControls;
  private model: THREE.Group = new THREE.Group();
  private overlay: THREE.Group = new THREE.Group();
  private solid: THREE.Mesh | null = null;
  private colors: ViewColors;
  private state: ModelState | null = null;
  private pickables: PickEdge[] = [];
  private edgeFilter: (e: PickEdge) => boolean = () => true;
  private pickMode: PickMode = 'none';
  private hover: Hit | null = null;
  private selectedEdges: { edge: PickEdge; side: 'top' | 'bottom' }[] = [];
  private measureSel: MeasureSelectionView[] = [];
  private measureLines: MeasureLine[] = [];
  private labels: { el: HTMLDivElement; at: V3 }[] = [];
  private labelLayer: HTMLDivElement;
  private vertexCache: V3[] | null = null;
  private dotTexture: THREE.Texture | null = null;
  private highlightFeature: string | null = null;
  private frameRequested = false;
  private downAt: { x: number; y: number } | null = null;
  private fitted = false;
  private sketchGroup: THREE.Group = new THREE.Group();
  private sketchLabels: { el: HTMLElement; at: V3 }[] = [];
  private sketchHandler: SketchPointerHandler | null = null;
  private pointers = new Set<number>();
  private viewChanged = false;
  private rendering = false;
  onPick: (hit: Hit | null, ev: PointerEvent) => void = () => {};
  onHover: (hit: Hit | null) => void = () => {};
  /** Called before a frame is drawn after the camera moved (sketch arrows are sized in pixels). */
  onViewChange: () => void = () => {};

  constructor(private host: HTMLElement, colors: ViewColors) {
    this.colors = colors;
    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, preserveDrawingBuffer: true });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    this.renderer.setClearColor(0x000000, 0);
    host.appendChild(this.renderer.domElement);
    this.labelLayer = document.createElement('div');
    this.labelLayer.className = 'ms-labels';
    Object.assign(this.labelLayer.style, { position: 'absolute', inset: '0', overflow: 'hidden', pointerEvents: 'none' });
    // first child: the stage's own overlays (hint, view buttons) stay above the labels
    host.prepend(this.labelLayer);
    this.camera = new THREE.OrthographicCamera(-1, 1, 1, -1, -100000, 100000);
    this.camera.position.set(1, 0.8, 1).multiplyScalar(500);
    this.camera.up.set(0, 1, 0);
    this.scene.add(this.camera);
    this.scene.add(new THREE.AmbientLight(0xffffff, 1.1));
    const hemi = new THREE.HemisphereLight(0xffffff, 0x445566, 0.9);
    this.scene.add(hemi);
    const key = new THREE.DirectionalLight(0xffffff, 1.6);
    key.position.set(0.6, 1, 0.8);
    this.camera.add(key);
    const fillLight = new THREE.DirectionalLight(0xffffff, 0.5);
    fillLight.position.set(-1, -0.4, 0.4);
    this.camera.add(fillLight);
    this.scene.add(this.model, this.overlay, this.sketchGroup);

    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.enableDamping = false;
    this.controls.screenSpacePanning = true;
    this.controls.zoomToCursor = true;
    this.controls.mouseButtons = { LEFT: THREE.MOUSE.ROTATE, MIDDLE: THREE.MOUSE.PAN, RIGHT: THREE.MOUSE.PAN };
    this.controls.addEventListener('change', () => {
      this.viewChanged = true;
      this.requestRender();
    });

    const el = this.renderer.domElement;
    el.addEventListener('pointerdown', (e) => {
      this.pointers.add(e.pointerId);
      const sk = this.sketchHandler;
      if (sk) {
        if (this.pointers.size > 1) sk.cancel();
        else if (e.button === 0) sk.down(e.clientX, e.clientY, e);
        return;
      }
      this.downAt = { x: e.clientX, y: e.clientY };
    });
    el.addEventListener('pointercancel', (e) => {
      this.pointers.delete(e.pointerId);
      this.sketchHandler?.cancel();
    });
    el.addEventListener('pointerup', (e) => {
      const multi = this.pointers.size > 1;
      this.pointers.delete(e.pointerId);
      const sk = this.sketchHandler;
      if (sk) {
        if (e.button === 0 && !multi) sk.up(e.clientX, e.clientY, e);
        return;
      }
      if (!this.downAt) return;
      const moved = Math.hypot(e.clientX - this.downAt.x, e.clientY - this.downAt.y);
      this.downAt = null;
      if (moved > 5 || e.button !== 0) return;
      this.onPick(this.pickAt(e.clientX, e.clientY), e);
    });
    el.addEventListener('pointermove', (e) => {
      const sk = this.sketchHandler;
      if (sk) {
        if (this.pointers.size <= 1) sk.move(e.clientX, e.clientY, e);
        return;
      }
      if (this.downAt) return;
      const hit = this.pickAt(e.clientX, e.clientY);
      if (!sameHit(hit, this.hover)) {
        this.hover = hit;
        this.onHover(hit);
        this.updateOverlay();
      }
      el.style.cursor = hit ? 'pointer' : 'default';
    });
    el.addEventListener('pointerleave', () => {
      if (this.sketchHandler) {
        this.sketchHandler.leave();
        return;
      }
      if (this.hover) {
        this.hover = null;
        this.onHover(null);
        this.updateOverlay();
      }
    });
    el.addEventListener('contextmenu', (e) => e.preventDefault());
    new ResizeObserver(() => this.resize()).observe(host);
    this.resize();
  }

  setColors(c: ViewColors): void {
    this.colors = c;
    if (this.state) this.rebuild();
  }

  private resize(): void {
    const w = Math.max(1, this.host.clientWidth);
    const h = Math.max(1, this.host.clientHeight);
    this.renderer.setSize(w, h, false);
    const aspect = w / h;
    const half = 300;
    this.camera.left = -half * aspect;
    this.camera.right = half * aspect;
    this.camera.top = half;
    this.camera.bottom = -half;
    this.camera.updateProjectionMatrix();
    this.scene.traverse((o) => {
      const m = (o as { material?: unknown }).material;
      if (m instanceof LineMaterial) m.resolution.set(w, h);
    });
    this.viewChanged = true;
    this.requestRender();
  }

  requestRender(): void {
    if (this.frameRequested || this.rendering) return;
    this.frameRequested = true;
    requestAnimationFrame(() => {
      this.frameRequested = false;
      this.rendering = true;
      try {
        if (this.viewChanged) {
          this.viewChanged = false;
          this.onViewChange();
        }
        this.renderer.render(this.scene, this.camera);
        this.placeLabels();
      } finally {
        this.rendering = false;
      }
    });
  }

  setModel(mesh: PartMesh | null, part: Part | null, flat: boolean): void {
    this.state = mesh && part ? { mesh, part, flat } : null;
    this.vertexCache = null;
    this.rebuild();
    if (!this.fitted && this.state) {
      this.fitted = true;
      this.setView('iso');
    }
  }

  clear(): void {
    this.state = null;
    this.vertexCache = null;
    this.rebuild();
  }

  /** In 'measure' mode, pass the edges and corner edges: their end points are the snap vertices. */
  setPickMode(mode: PickMode, edges: PickEdge[] = [], filter: (e: PickEdge) => boolean = () => true): void {
    this.pickMode = mode;
    this.pickables = edges;
    this.edgeFilter = filter;
    this.vertexCache = null;
    this.hover = null;
    this.updateOverlay();
  }

  setSelection(edges: { edge: PickEdge; side: 'top' | 'bottom' }[]): void {
    this.selectedEdges = edges;
    this.updateOverlay();
  }

  /** Picked measure entities: vertices as dots, edges as fat lines, faces as a tint. */
  setMeasureSelection(items: MeasureSelectionView[]): void {
    this.measureSel = items;
    this.updateOverlay();
  }

  /** Dimension lines with a text label at each midpoint; [] removes them. */
  setMeasureOverlay(lines: MeasureLine[]): void {
    this.measureLines = lines;
    for (const l of this.labels) l.el.remove();
    this.labels = lines.map((l) => {
      const el = document.createElement('div');
      el.className = 'ms-label';
      el.textContent = l.text;
      Object.assign(el.style, { position: 'absolute', left: '0', top: '0', pointerEvents: 'none' });
      this.labelLayer.append(el);
      return { el, at: v3((l.a.x + l.b.x) / 2, (l.a.y + l.b.y) / 2, (l.a.z + l.b.z) / 2) };
    });
    this.placeLabels();
    this.updateOverlay();
  }

  /** Labels are HTML, so they follow the camera here instead of in the scene. */
  private placeLabels(): void {
    if (!this.labels.length && !this.sketchLabels.length) return;
    const c = this.renderer.domElement;
    const v = new THREE.Vector3();
    for (const l of [...this.labels, ...this.sketchLabels]) {
      v.set(l.at.x, l.at.y, l.at.z).project(this.camera);
      const x = Math.round(c.offsetLeft + ((v.x + 1) / 2) * c.clientWidth);
      const y = Math.round(c.offsetTop + ((1 - v.y) / 2) * c.clientHeight);
      l.el.style.transform = `translate(${x}px, ${y}px) translate(-50%, -50%)`;
    }
  }

  /**
   * Hand the left button and one-finger touches to a sketch (null gives them
   * back to rotating and picking). The middle button rotates and the right
   * button pans meanwhile, as in SolidWorks.
   */
  setSketchInput(h: SketchPointerHandler | null): void {
    this.sketchHandler = h;
    this.downAt = null;
    const el = this.renderer.domElement;
    if (h) {
      this.controls.mouseButtons = { LEFT: null, MIDDLE: THREE.MOUSE.ROTATE, RIGHT: THREE.MOUSE.PAN };
      this.controls.touches = { ONE: null, TWO: THREE.TOUCH.DOLLY_PAN };
      el.style.cursor = 'crosshair';
      if (this.hover) {
        this.hover = null;
        this.onHover(null);
        this.updateOverlay();
      }
    } else {
      this.controls.mouseButtons = { LEFT: THREE.MOUSE.ROTATE, MIDDLE: THREE.MOUSE.PAN, RIGHT: THREE.MOUSE.PAN };
      this.controls.touches = { ONE: THREE.TOUCH.ROTATE, TWO: THREE.TOUCH.DOLLY_PAN };
      el.style.cursor = 'default';
    }
  }

  /** Cursor over the viewport while sketching (crosshair, pointer over a pickable item, move while dragging). */
  setSketchCursor(cursor: string): void {
    if (this.sketchHandler) this.renderer.domElement.style.cursor = cursor;
  }

  /** Replace the sketch graphics; null removes them. */
  setSketchOverlay(o: SketchOverlay | null): void {
    this.disposeGroup(this.sketchGroup);
    const keep = new Set(o?.labels.map((l) => l.el) ?? []);
    for (const l of this.sketchLabels) if (!keep.has(l.el)) l.el.remove();
    this.sketchLabels = o?.labels ?? [];
    for (const l of this.sketchLabels) {
      if (l.el.parentElement !== this.labelLayer) {
        Object.assign(l.el.style, { position: 'absolute', left: '0', top: '0' });
        this.labelLayer.append(l.el);
      }
    }
    if (o) {
      for (const l of o.lines) {
        if (!l.seg.length) continue;
        const line = this.fatLines(l.seg, this.fatMaterial(l.color, l.width, { dashed: l.dashed, depthTest: !l.onTop }));
        if (l.onTop) line.renderOrder = OVERLAY_ORDER;
        this.sketchGroup.add(line);
      }
      for (const p of o.points) if (p.pts.length) this.sketchGroup.add(this.dots(p.pts, p.color, p.size));
    }
    this.placeLabels();
    this.requestRender();
  }

  /** Client position of a world point. */
  worldToScreen(p: V3): { x: number; y: number } {
    const s = this.toScreen(p);
    return { x: s.x, y: s.y };
  }

  /** Where the view ray through a client position meets a plane; null when the plane is seen edge-on. */
  screenToPlane(x: number, y: number, origin: V3, normal: V3): V3 | null {
    const ray = new THREE.Raycaster();
    ray.setFromCamera(this.ndc(x, y), this.camera);
    const n = new THREE.Vector3(normal.x, normal.y, normal.z).normalize();
    if (Math.abs(n.dot(ray.ray.direction)) < 1e-6) return null;
    const plane = new THREE.Plane().setFromNormalAndCoplanarPoint(n, new THREE.Vector3(origin.x, origin.y, origin.z));
    // the orthographic ray starts at the camera plane: intersect the whole line, not just the half ray
    const t = -(plane.distanceToPoint(ray.ray.origin) / n.dot(ray.ray.direction));
    const q = ray.ray.origin.clone().addScaledVector(ray.ray.direction, t);
    return v3(q.x, q.y, q.z);
  }

  /** The sheet surface of a face (placement M) that faces the camera. */
  facingSide(M: Affine | undefined): 'top' | 'bottom' {
    if (!M) return 'top';
    const n = applyDirA(M, v3(0, 0, 1));
    const toCam = new THREE.Vector3().subVectors(this.camera.position, this.controls.target);
    return toCam.x * n.x + toCam.y * n.y + toCam.z * n.z >= 0 ? 'top' : 'bottom';
  }

  /** Whether the part hides a world point from the camera. */
  isHidden(p: V3): boolean {
    return this.occluded(p, this.occlusionTol());
  }

  /** World length of one screen pixel. */
  pixelSize(): number {
    const h = Math.max(1, this.renderer.domElement.clientHeight);
    return (this.camera.top - this.camera.bottom) / this.camera.zoom / h;
  }

  /**
   * Look straight onto a plane ("Normal auf"): `normal` points towards the
   * viewer, `up` becomes screen up; `pts` (world) are zoomed to fill the view.
   */
  lookNormal(normal: V3, up: V3, pts: V3[]): void {
    const box = new THREE.Box3().setFromPoints(pts.map((p) => new THREE.Vector3(p.x, p.y, p.z)));
    const sphere = box.getBoundingSphere(new THREE.Sphere());
    const r = Math.max(sphere.radius, 10);
    const all = this.bounds().getBoundingSphere(new THREE.Sphere());
    const dir = new THREE.Vector3(normal.x, normal.y, normal.z).normalize();
    this.camera.up.set(up.x, up.y, up.z).normalize();
    this.syncUp();
    this.controls.target.copy(sphere.center);
    // outside the whole part, so the ray origin of the orthographic raycaster lies in front of it
    this.camera.position.copy(sphere.center).addScaledVector(dir, Math.max(all.radius, r) * 4 + 200);
    this.camera.lookAt(sphere.center);
    const w = this.camera.right - this.camera.left;
    const h = this.camera.top - this.camera.bottom;
    this.camera.zoom = Math.min(w, h) / (2.6 * r);
    const reach = Math.max(all.radius, r) * 50 + 10000;
    this.camera.near = -reach;
    this.camera.far = reach;
    this.camera.updateProjectionMatrix();
    this.controls.update();
    this.viewChanged = true;
    this.requestRender();
  }

  /** OrbitControls caches the up direction at construction; orbiting after a view with another up needs it refreshed. */
  private syncUp(): void {
    const c = this.controls as unknown as { _quat?: THREE.Quaternion; _quatInverse?: THREE.Quaternion };
    if (!c._quat || !c._quatInverse) return;
    c._quat.setFromUnitVectors(this.camera.up, new THREE.Vector3(0, 1, 0));
    c._quatInverse.copy(c._quat).invert();
  }

  setHighlightFeature(id: string | null): void {
    this.highlightFeature = id;
    this.updateOverlay();
  }

  private disposeGroup(g: THREE.Group): void {
    g.traverse((o) => {
      const m = o as THREE.Mesh;
      if (m.geometry) m.geometry.dispose();
      const mat = (m as { material?: THREE.Material | THREE.Material[] }).material;
      if (Array.isArray(mat)) mat.forEach((x) => x.dispose());
      else mat?.dispose();
    });
    g.clear();
  }

  private fatMaterial(color: string, width: number, opts: Partial<{ dashed: boolean; depthTest: boolean; opacity: number }> = {}): LineMaterial {
    const m = new LineMaterial({
      color: new THREE.Color(color).getHex(),
      linewidth: width,
      dashed: opts.dashed ?? false,
      dashSize: 4,
      gapSize: 2.5,
      transparent: (opts.opacity ?? 1) < 1,
      opacity: opts.opacity ?? 1,
      depthTest: opts.depthTest ?? true,
    });
    m.resolution.set(Math.max(1, this.host.clientWidth), Math.max(1, this.host.clientHeight));
    return m;
  }

  private fatLines(segments: number[], mat: LineMaterial): LineSegments2 {
    const g = new LineSegmentsGeometry();
    g.setPositions(segments);
    const l = new LineSegments2(g, mat);
    l.computeLineDistances();
    return l;
  }

  private rebuild(): void {
    this.disposeGroup(this.model);
    this.solid = null;
    if (!this.state) {
      this.updateOverlay();
      return;
    }
    const { mesh, flat } = this.state;
    const src = flat ? mesh.flatPositions : mesh.positions;
    // non-indexed copy: sheet surfaces get exact normals, side walls flat ones
    const nrmSrc = flat ? mesh.flatNormals : mesh.normals;
    const triCount = mesh.indices.length / 3;
    const pos = new Float32Array(triCount * 9);
    const nrm = new Float32Array(triCount * 9);
    for (let t = 0; t < triCount; t++) {
      const ia = mesh.indices[3 * t] * 3;
      const ib = mesh.indices[3 * t + 1] * 3;
      const ic = mesh.indices[3 * t + 2] * 3;
      for (let k = 0; k < 3; k++) {
        pos[9 * t + k] = src[ia + k];
        pos[9 * t + 3 + k] = src[ib + k];
        pos[9 * t + 6 + k] = src[ic + k];
      }
      if (mesh.triSurface[t] === 2) {
        const ux = src[ib] - src[ia], uy = src[ib + 1] - src[ia + 1], uz = src[ib + 2] - src[ia + 2];
        const vx = src[ic] - src[ia], vy = src[ic + 1] - src[ia + 1], vz = src[ic + 2] - src[ia + 2];
        let nx = uy * vz - uz * vy;
        let ny = uz * vx - ux * vz;
        let nz = ux * vy - uy * vx;
        const l = Math.hypot(nx, ny, nz) || 1;
        nx /= l;
        ny /= l;
        nz /= l;
        for (let c = 0; c < 3; c++) {
          nrm[9 * t + 3 * c] = nx;
          nrm[9 * t + 3 * c + 1] = ny;
          nrm[9 * t + 3 * c + 2] = nz;
        }
      } else {
        for (let k = 0; k < 3; k++) {
          nrm[9 * t + k] = nrmSrc[ia + k];
          nrm[9 * t + 3 + k] = nrmSrc[ib + k];
          nrm[9 * t + 6 + k] = nrmSrc[ic + k];
        }
      }
    }
    const shaded = new THREE.BufferGeometry();
    shaded.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    shaded.setAttribute('normal', new THREE.BufferAttribute(nrm, 3));
    const mat = new THREE.MeshStandardMaterial({
      color: new THREE.Color(this.colors.steel),
      metalness: 0.3,
      roughness: 0.5,
      // constant offset only: a slope-scaled one lets hidden outline edges bleed through at grazing angles
      polygonOffset: true,
      polygonOffsetFactor: 0,
      polygonOffsetUnits: 2,
    });
    this.solid = new THREE.Mesh(shaded, mat);
    this.model.add(this.solid);

    const edges = new THREE.BufferGeometry();
    edges.setAttribute('position', new THREE.BufferAttribute(Float32Array.from(src), 3));
    edges.setIndex(new THREE.BufferAttribute(mesh.edgeLines, 1));
    this.model.add(new THREE.LineSegments(edges, new THREE.LineBasicMaterial({ color: new THREE.Color(this.colors.edge) })));

    this.updateOverlay();
  }

  /** Bend lines in the flat pattern, drawn on both sides of the sheet. */
  setFlatBendLines(fp: FlatPattern | null, part: Part | null): void {
    const old = this.model.getObjectByName('bendlines');
    if (old) {
      this.model.remove(old);
      this.disposeGroup(old as THREE.Group);
    }
    if (!fp || !part || !this.state?.flat) return this.requestRender();
    const group = new THREE.Group();
    group.name = 'bendlines';
    const M = part.anchor.M;
    for (const up of [true, false]) {
      const seg: number[] = [];
      for (const b of fp.bendLines) {
        if (b.up !== up) continue;
        for (const hgt of [part.T + 0.02, -0.02]) {
          const a = applyA(M, v3(b.a.x, b.a.y, hgt));
          const c = applyA(M, v3(b.b.x, b.b.y, hgt));
          seg.push(a.x, a.y, a.z, c.x, c.y, c.z);
        }
      }
      if (seg.length) group.add(this.fatLines(seg, this.fatMaterial(up ? this.colors.up : this.colors.down, 2.2, { dashed: true })));
    }
    this.model.add(group);
    this.requestRender();
  }

  private edgeSegment(e: PickEdge, side: 'top' | 'bottom'): [V3, V3] {
    const s = this.state?.flat ? e.flat : e.folded;
    return side === 'top' ? [s[0], s[1]] : [s[2], s[3]];
  }

  private updateOverlay(): void {
    this.disposeGroup(this.overlay);
    if (this.state && this.highlightFeature) {
      this.overlay.add(this.featureHighlight(this.highlightFeature));
    }
    const sel: number[] = [];
    for (const s of this.selectedEdges) {
      const [a, b] = this.edgeSegment(s.edge, s.side);
      sel.push(a.x, a.y, a.z, b.x, b.y, b.z);
    }
    if (sel.length) this.overlay.add(this.fatLines(sel, this.fatMaterial(this.colors.accent, 4, { depthTest: false })));
    this.addMeasureSelection();
    this.addMeasureLines();
    if (this.hover?.kind === 'vertex') {
      this.overlay.add(this.dots([this.hover.p], this.colors.sel, 12));
    } else if (this.hover?.kind === 'edge') {
      const runs = this.pickMode === 'measure' ? this.edgeRun(this.hover.edge) : [this.hover.edge];
      const seg = this.segments(runs, this.hover.side);
      this.overlay.add(this.fatLines(seg, this.fatMaterial(this.colors.sel, 4.5, { depthTest: false })));
    } else if (this.hover?.kind === 'face' && this.state && this.pickMode !== 'edge') {
      this.overlay.add(this.pieceHighlight(this.hover.pieceKind, this.hover.id, this.colors.sel, 0.28));
    }
    this.requestRender();
  }

  private segments(edges: PickEdge[], side: 'top' | 'bottom'): number[] {
    const out: number[] = [];
    for (const e of edges) {
      const [a, b] = this.edgeSegment(e, side);
      out.push(a.x, a.y, a.z, b.x, b.y, b.z);
    }
    return out;
  }

  /** A round hole measures as one circle, so all its segments light up together. */
  private edgeRun(e: PickEdge): PickEdge[] {
    if (!e.hole || !this.state?.part.circles.has(e.tag)) return [e];
    const run = this.pickables.filter((o) => o.hole && o.tag === e.tag && o.faceId === e.faceId);
    return run.length ? run : [e];
  }

  private addMeasureSelection(): void {
    const seg: number[] = [];
    const pts: V3[] = [];
    for (const s of this.measureSel) {
      if (s.kind === 'vertex') pts.push(s.p);
      else if (s.kind === 'edge') seg.push(...this.segments(this.edgeRun(s.edge), s.side));
      else if (this.state) this.overlay.add(this.pieceHighlight(s.pieceKind, s.id, this.colors.accent, 0.32));
    }
    if (seg.length) {
      const l = this.fatLines(seg, this.fatMaterial(this.colors.accent, 4, { depthTest: false }));
      l.renderOrder = OVERLAY_ORDER;
      this.overlay.add(l);
    }
    if (pts.length) this.overlay.add(this.dots(pts, this.colors.accent, 11));
  }

  private addMeasureLines(): void {
    if (!this.measureLines.length) return;
    const seg: number[] = [];
    const ends: V3[] = [];
    for (const l of this.measureLines) {
      seg.push(l.a.x, l.a.y, l.a.z, l.b.x, l.b.y, l.b.z);
      ends.push(l.a, l.b);
    }
    const lines = this.fatLines(seg, this.fatMaterial(this.colors.sel, 2.2, { depthTest: false }));
    lines.renderOrder = OVERLAY_ORDER;
    this.overlay.add(lines, this.dots(ends, this.colors.sel, 8));
  }

  /** Screen-sized round markers that stay visible through the part. */
  private dots(pts: V3[], color: string, size: number): THREE.Points {
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pts.flatMap((p) => [p.x, p.y, p.z]), 3));
    const m = new THREE.PointsMaterial({
      color: new THREE.Color(color),
      size,
      sizeAttenuation: false,
      map: this.dotMap(),
      transparent: true,
      alphaTest: 0.05,
      depthTest: false,
      depthWrite: false,
    });
    const p = new THREE.Points(g, m);
    p.renderOrder = OVERLAY_ORDER + 1;
    return p;
  }

  /** White disc with a dark rim; the material colour tints it. Shared, so never disposed with the overlay. */
  private dotMap(): THREE.Texture {
    if (this.dotTexture) return this.dotTexture;
    const c = document.createElement('canvas');
    c.width = c.height = 64;
    const g = c.getContext('2d');
    if (g) {
      g.fillStyle = '#404040';
      g.beginPath();
      g.arc(32, 32, 31, 0, 2 * Math.PI);
      g.fill();
      g.fillStyle = '#ffffff';
      g.beginPath();
      g.arc(32, 32, 21, 0, 2 * Math.PI);
      g.fill();
    }
    this.dotTexture = new THREE.CanvasTexture(c);
    return this.dotTexture;
  }

  private trianglesWhere(pred: (piece: number) => boolean): number[] {
    const { mesh, flat } = this.state!;
    const src = flat ? mesh.flatPositions : mesh.positions;
    const out: number[] = [];
    for (let t = 0; t < mesh.triPiece.length; t++) {
      if (!pred(mesh.triPiece[t])) continue;
      for (let k = 0; k < 3; k++) {
        const i = mesh.indices[3 * t + k] * 3;
        out.push(src[i], src[i + 1], src[i + 2]);
      }
    }
    return out;
  }

  private overlayMesh(pos: number[], color: string, opacity: number): THREE.Mesh {
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    return new THREE.Mesh(
      g,
      new THREE.MeshBasicMaterial({
        color: new THREE.Color(color),
        transparent: true,
        opacity,
        depthWrite: false,
        polygonOffset: true,
        polygonOffsetFactor: -2,
        polygonOffsetUnits: -2,
        side: THREE.DoubleSide,
      }),
    );
  }

  private pieceHighlight(kind: 'face' | 'bend', id: string, color: string, opacity: number): THREE.Mesh {
    const pieces = this.state!.mesh.pieces;
    return this.overlayMesh(this.trianglesWhere((p) => pieces[p].kind === kind && pieces[p].id === id), color, opacity);
  }

  private featureHighlight(feature: string): THREE.Mesh {
    const pieces = this.state!.mesh.pieces;
    return this.overlayMesh(this.trianglesWhere((p) => pieces[p].feature === feature), this.colors.sel, 0.3);
  }

  private ndc(x: number, y: number): THREE.Vector2 {
    const r = this.renderer.domElement.getBoundingClientRect();
    return new THREE.Vector2(((x - r.left) / r.width) * 2 - 1, -((y - r.top) / r.height) * 2 + 1);
  }

  private toScreen(p: V3, r: DOMRect = this.renderer.domElement.getBoundingClientRect()): { x: number; y: number; depth: number } {
    const v = new THREE.Vector3(p.x, p.y, p.z).project(this.camera);
    return { x: r.left + ((v.x + 1) / 2) * r.width, y: r.top + ((1 - v.y) / 2) * r.height, depth: v.z };
  }

  private raycastSolid(x: number, y: number): THREE.Intersection | null {
    if (!this.solid) return null;
    const ray = new THREE.Raycaster();
    ray.setFromCamera(this.ndc(x, y), this.camera);
    return ray.intersectObject(this.solid, false)[0] ?? null;
  }

  private pickAt(x: number, y: number): Hit | null {
    if (!this.state || this.pickMode === 'none') return null;
    if (this.pickMode === 'edge') return this.pickEdge(x, y, EDGE_PX, this.edgeFilter);
    if (this.pickMode === 'measure') {
      const face = this.pickFace(x, y);
      const snap = face ? this.snapRadius(face, x, y) : MEASURE_EDGE_PX;
      return this.pickVertex(x, y, Math.min(VERTEX_PX, snap)) ?? this.pickEdge(x, y, snap, () => true) ?? face;
    }
    const face = this.pickFace(x, y);
    // commands that ask for a face take flat faces only: a bend under the cursor is not clickable
    return this.pickMode === 'face' && face?.pieceKind === 'bend' ? null : face;
  }

  /** Measure snap radius for edges while the cursor is over `face`: smaller inside narrow pieces. */
  private snapRadius(face: FaceHit, x: number, y: number): number {
    // a thin side wall between two edges is nearly always meant as one of them
    if (face.surface === 2) return MEASURE_EDGE_PX;
    const across = this.towardNearestBorder(face, x, y);
    if (!across) return MEASURE_EDGE_PX;
    // beyond this width the full radius applies anyway
    const cap = MEASURE_EDGE_PX / NARROW_SNAP_SHARE;
    const width = this.extent(face, x, y, across, cap) + this.extent(face, x, y, { x: -across.x, y: -across.y }, cap);
    return Math.min(MEASURE_EDGE_PX, Math.max(MIN_SNAP_PX, NARROW_SNAP_SHARE * width));
  }

  /** Screen direction from the cursor to the nearest border edge of the hit piece (its seams, for a bend). */
  private towardNearestBorder(face: FaceHit, x: number, y: number): V2 | null {
    const r = this.renderer.domElement.getBoundingClientRect();
    const seam = `seam:${face.id}:`;
    const own = face.pieceKind === 'bend' ? (e: PickEdge) => e.tag.startsWith(seam) : (e: PickEdge) => !e.corner && e.faceId === face.id;
    let best: V2 | null = null;
    let bd = 4 * MEASURE_EDGE_PX;
    for (const e of this.pickables) {
      if (!own(e)) continue;
      for (const side of ['top', 'bottom'] as const) {
        const [a, b] = this.edgeSegment(e, side);
        const sa = this.toScreen(a, r);
        const sb = this.toScreen(b, r);
        const ux = sb.x - sa.x;
        const uy = sb.y - sa.y;
        const l2 = ux * ux + uy * uy;
        const t = l2 > 0 ? Math.max(0, Math.min(1, ((x - sa.x) * ux + (y - sa.y) * uy) / l2)) : 0;
        const dx = sa.x + ux * t - x;
        const dy = sa.y + uy * t - y;
        const d = Math.hypot(dx, dy);
        if (d > 1e-6 && d < bd) {
          bd = d;
          best = { x: dx / d, y: dy / d };
        }
      }
    }
    return best;
  }

  /**
   * How far (in px, up to `cap`) the hit piece continues from the cursor in
   * screen direction `dir`. Raycasts, so a curved surface ends at its
   * silhouette and a piece also ends where another one covers it.
   */
  private extent(face: FaceHit, x: number, y: number, dir: V2, cap: number): number {
    const same = (t: number): boolean => {
      const h = this.pickFace(x + dir.x * t, y + dir.y * t);
      return !!h && h.pieceKind === face.pieceKind && h.id === face.id;
    };
    let inside = 0;
    let outside = 1;
    while (same(outside)) {
      inside = outside;
      if (outside >= cap) return cap;
      outside = Math.min(cap, outside * 2);
    }
    while (outside - inside > 0.5) {
      const m = (inside + outside) / 2;
      if (same(m)) inside = m;
      else outside = m;
    }
    return inside;
  }

  private pickEdge(x: number, y: number, maxPx: number, filter: (e: PickEdge) => boolean): EdgeHit | null {
    const r = this.renderer.domElement.getBoundingClientRect();
    const candidates: { d: number; hit: EdgeHit; p: V3 }[] = [];
    for (const e of this.pickables) {
      if (!filter(e)) continue;
      for (const side of ['top', 'bottom'] as const) {
        const [a, b] = this.edgeSegment(e, side);
        const sa = this.toScreen(a, r);
        const sb = this.toScreen(b, r);
        const dx = sb.x - sa.x;
        const dy = sb.y - sa.y;
        const l2 = dx * dx + dy * dy;
        let t = l2 > 0 ? ((x - sa.x) * dx + (y - sa.y) * dy) / l2 : 0;
        t = Math.max(0, Math.min(1, t));
        const d = Math.hypot(x - (sa.x + dx * t), y - (sa.y + dy * t));
        if (d > maxPx) continue;
        const p = v3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        candidates.push({ d, hit: { kind: 'edge', edge: e, side }, p });
      }
    }
    candidates.sort((u, w) => u.d - w.d);
    const tol = this.occlusionTol();
    for (const c of candidates.slice(0, 12)) {
      if (!this.occluded(c.p, tol)) return c.hit;
    }
    return null;
  }

  private pickVertex(x: number, y: number, maxPx = VERTEX_PX): VertexHit | null {
    const r = this.renderer.domElement.getBoundingClientRect();
    const candidates: { d: number; p: V3 }[] = [];
    for (const p of this.measureVertices()) {
      const s = this.toScreen(p, r);
      const d = Math.hypot(x - s.x, y - s.y);
      if (d <= maxPx) candidates.push({ d, p });
    }
    candidates.sort((u, w) => u.d - w.d);
    const tol = this.occlusionTol();
    for (const c of candidates.slice(0, 12)) {
      if (!this.occluded(c.p, tol)) return { kind: 'vertex', p: c.p };
    }
    return null;
  }

  private measureVertices(): V3[] {
    if (!this.vertexCache) {
      const st = this.state;
      this.vertexCache = st ? snapVertices(this.pickables, st.flat, st.part.circles) : [];
    }
    return this.vertexCache;
  }

  private occlusionTol(): number {
    return Math.max(0.5, 0.75 * (this.state?.part.T ?? 1));
  }

  /** Whether solid material lies clearly in front of a point (as seen by the camera). */
  private occluded(p: V3, tol: number): boolean {
    if (!this.solid) return false;
    const s = this.toScreen(p);
    const ray = new THREE.Raycaster();
    ray.setFromCamera(this.ndc(s.x, s.y), this.camera);
    const hit = ray.intersectObject(this.solid, false)[0];
    if (!hit) return false;
    const along = new THREE.Vector3(p.x, p.y, p.z).sub(ray.ray.origin).dot(ray.ray.direction);
    return hit.distance < along - tol;
  }

  private pickFace(x: number, y: number): FaceHit | null {
    const hit = this.raycastSolid(x, y);
    if (!hit || hit.faceIndex === undefined || hit.faceIndex === null) return null;
    const { mesh, part, flat } = this.state!;
    const piece = mesh.pieces[mesh.triPiece[hit.faceIndex]];
    if (!piece) return null;
    const out: FaceHit = {
      kind: 'face',
      pieceKind: piece.kind,
      id: piece.id,
      feature: piece.feature,
      surface: mesh.triSurface[hit.faceIndex] as Surface,
      point: v3(hit.point.x, hit.point.y, hit.point.z),
    };
    if (piece.kind === 'face') {
      const M: Affine | undefined = flat ? part.anchor.M : mesh.placements.get(piece.id);
      if (M) {
        const local = applyA(invertRigid(M), out.point);
        out.flat = { x: local.x, y: local.y };
      }
    }
    return out;
  }

  /** Standard views; 'normal' looks onto the fixed face. */
  setView(name: ViewName): void {
    const box = this.bounds();
    const center = box.getCenter(new THREE.Vector3());
    let dir = new THREE.Vector3(1, 0.8, 1);
    let up = new THREE.Vector3(0, 1, 0);
    if (name === 'top') {
      dir = new THREE.Vector3(0, 1, 0);
      up = new THREE.Vector3(0, 0, -1);
    } else if (name === 'front') dir = new THREE.Vector3(0, 0, 1);
    else if (name === 'right') dir = new THREE.Vector3(1, 0, 0);
    else if (name === 'normal' && this.state) {
      const M = this.state.part.anchor.M;
      const n = applyDirA(M, v3(0, 0, 1));
      const ey = applyDirA(M, v3(0, 1, 0));
      dir = new THREE.Vector3(n.x, n.y, n.z);
      up = new THREE.Vector3(ey.x, ey.y, ey.z);
    }
    dir.normalize();
    const size = box.getSize(new THREE.Vector3()).length() || 200;
    this.camera.position.copy(center).addScaledVector(dir, size * 2 + 100);
    this.camera.up.copy(up);
    this.syncUp();
    this.controls.target.copy(center);
    this.camera.lookAt(center);
    this.fit();
  }

  fit(): void {
    const box = this.bounds();
    const sphere = box.getBoundingSphere(new THREE.Sphere());
    const r = Math.max(sphere.radius, 10);
    const w = this.camera.right - this.camera.left;
    const h = this.camera.top - this.camera.bottom;
    this.camera.zoom = Math.min(w, h) / (2.3 * r);
    const dir = new THREE.Vector3().subVectors(this.camera.position, this.controls.target).normalize();
    this.controls.target.copy(sphere.center);
    this.camera.position.copy(sphere.center).addScaledVector(dir, r * 4 + 200);
    this.camera.near = -r * 50 - 10000;
    this.camera.far = r * 50 + 10000;
    this.camera.updateProjectionMatrix();
    this.controls.update();
    this.viewChanged = true;
    this.requestRender();
  }

  private bounds(): THREE.Box3 {
    if (!this.solid) return new THREE.Box3(new THREE.Vector3(-100, -100, -100), new THREE.Vector3(100, 100, 100));
    this.solid.geometry.computeBoundingBox();
    return this.solid.geometry.boundingBox!.clone();
  }

  snapshot(): string {
    this.renderer.render(this.scene, this.camera);
    return this.renderer.domElement.toDataURL('image/png');
  }
}

function sameHit(a: Hit | null, b: Hit | null): boolean {
  if (!a || !b) return a === b;
  if (a.kind === 'vertex' && b.kind === 'vertex') return a.p.x === b.p.x && a.p.y === b.p.y && a.p.z === b.p.z;
  if (a.kind === 'edge' && b.kind === 'edge') return a.edge === b.edge && a.side === b.side;
  if (a.kind === 'face' && b.kind === 'face') return a.id === b.id && a.pieceKind === b.pieceKind && a.surface === b.surface;
  return false;
}

interface VertexUse {
  tags: Set<string>;
  dirs: V2[];
  top: V3;
  bottom: V3;
}

/**
 * Ring corners on both sheet surfaces, deduplicated. Inner points of round
 * holes and tessellated arcs are left out: they are not corners, and their
 * snap radius would otherwise cover the whole curve and hide the edge.
 */
function snapVertices(edges: PickEdge[], flat: boolean, circles: Map<string, CircleInfo>): V3[] {
  const uses = new Map<string, VertexUse>();
  const visit = (e: PickEdge, p: V2, dir: V2, top: V3, bottom: V3): void => {
    const k = `${e.faceId}|${p.x.toFixed(5)}|${p.y.toFixed(5)}`;
    let u = uses.get(k);
    if (!u) uses.set(k, (u = { tags: new Set(), dirs: [], top, bottom }));
    u.tags.add(e.tag);
    u.dirs.push(dir);
  };
  for (const e of edges) {
    // corner edges join ring points that the outline edges already cover
    if (e.corner) continue;
    const s = flat ? e.flat : e.folded;
    const d = sub2(e.flatB, e.flatA);
    visit(e, e.flatA, d, s[0], s[2]);
    visit(e, e.flatB, d, s[1], s[3]);
  }
  const out = new Map<string, V3>();
  for (const u of uses.values()) {
    if (isSmooth(u, circles)) continue;
    for (const p of [u.top, u.bottom]) out.set(`${p.x.toFixed(4)}|${p.y.toFixed(4)}|${p.z.toFixed(4)}`, p);
  }
  return [...out.values()];
}

function isSmooth(u: VertexUse, circles: Map<string, CircleInfo>): boolean {
  if (u.tags.size !== 1) return false;
  const [tag] = u.tags;
  if (circles.has(tag)) return true;
  if (u.dirs.length !== 2) return false;
  const [a, b] = u.dirs;
  return Math.abs(Math.atan2(cross2(a, b), dot2(a, b))) < SMOOTH_TURN;
}
