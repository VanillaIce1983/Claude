// three.js viewport: shaded part with crisp edges, edge/face picking,
// selection highlights, flat-pattern bend lines and standard views.

import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import { LineMaterial } from 'three/examples/jsm/lines/LineMaterial.js';
import { LineSegments2 } from 'three/examples/jsm/lines/LineSegments2.js';
import { LineSegmentsGeometry } from 'three/examples/jsm/lines/LineSegmentsGeometry.js';
import { toCreasedNormals } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import type { FlatPattern } from '../kernel/flat';
import type { PartMesh, PickEdge } from '../kernel/mesh';
import type { Part } from '../kernel/model';
import { Affine, V3, applyA, applyDirA, invertRigid, v3 } from '../kernel/vec';

export type PickMode = 'none' | 'edge' | 'face';

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
  /** Flat coordinates of the hit point (faces only). */
  flat?: { x: number; y: number };
}

export type Hit = EdgeHit | FaceHit;

export interface ViewColors {
  steel: string;
  edge: string;
  sel: string;
  accent: string;
  up: string;
  down: string;
}

export type ViewName = 'iso' | 'top' | 'front' | 'right' | 'normal';

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
  private highlightFeature: string | null = null;
  private frameRequested = false;
  private downAt: { x: number; y: number } | null = null;
  private fitted = false;
  onPick: (hit: Hit | null, ev: PointerEvent) => void = () => {};
  onHover: (hit: Hit | null) => void = () => {};

  constructor(private host: HTMLElement, colors: ViewColors) {
    this.colors = colors;
    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, preserveDrawingBuffer: true });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    this.renderer.setClearColor(0x000000, 0);
    host.appendChild(this.renderer.domElement);
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
    this.scene.add(this.model, this.overlay);

    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.enableDamping = false;
    this.controls.screenSpacePanning = true;
    this.controls.zoomToCursor = true;
    this.controls.mouseButtons = { LEFT: THREE.MOUSE.ROTATE, MIDDLE: THREE.MOUSE.PAN, RIGHT: THREE.MOUSE.PAN };
    this.controls.addEventListener('change', () => this.requestRender());

    const el = this.renderer.domElement;
    el.addEventListener('pointerdown', (e) => {
      this.downAt = { x: e.clientX, y: e.clientY };
    });
    el.addEventListener('pointerup', (e) => {
      if (!this.downAt) return;
      const moved = Math.hypot(e.clientX - this.downAt.x, e.clientY - this.downAt.y);
      this.downAt = null;
      if (moved > 5 || e.button !== 0) return;
      this.onPick(this.pickAt(e.clientX, e.clientY), e);
    });
    el.addEventListener('pointermove', (e) => {
      if (this.downAt) return;
      const hit = this.pickAt(e.clientX, e.clientY);
      if (!sameHit(hit, this.hover)) {
        this.hover = hit;
        this.onHover(hit);
        this.updateOverlay();
      }
      el.style.cursor = hit && (this.pickMode !== 'none' || hit.kind === 'face') ? 'pointer' : 'default';
    });
    el.addEventListener('pointerleave', () => {
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
    this.requestRender();
  }

  requestRender(): void {
    if (this.frameRequested) return;
    this.frameRequested = true;
    requestAnimationFrame(() => {
      this.frameRequested = false;
      this.renderer.render(this.scene, this.camera);
    });
  }

  setModel(mesh: PartMesh | null, part: Part | null, flat: boolean): void {
    this.state = mesh && part ? { mesh, part, flat } : null;
    this.rebuild();
    if (!this.fitted && this.state) {
      this.fitted = true;
      this.setView('iso');
    }
  }

  clear(): void {
    this.state = null;
    this.rebuild();
  }

  setPickMode(mode: PickMode, edges: PickEdge[] = [], filter: (e: PickEdge) => boolean = () => true): void {
    this.pickMode = mode;
    this.pickables = edges;
    this.edgeFilter = filter;
    this.hover = null;
    this.updateOverlay();
  }

  setSelection(edges: { edge: PickEdge; side: 'top' | 'bottom' }[]): void {
    this.selectedEdges = edges;
    this.updateOverlay();
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
    const indexed = new THREE.BufferGeometry();
    indexed.setAttribute('position', new THREE.BufferAttribute(Float32Array.from(src), 3));
    indexed.setIndex(new THREE.BufferAttribute(mesh.indices, 1));
    const shaded = toCreasedNormals(indexed, (30 * Math.PI) / 180);
    const mat = new THREE.MeshStandardMaterial({
      color: new THREE.Color(this.colors.steel),
      metalness: 0.3,
      roughness: 0.5,
      polygonOffset: true,
      polygonOffsetFactor: 1,
      polygonOffsetUnits: 1,
    });
    this.solid = new THREE.Mesh(shaded, mat);
    this.model.add(this.solid);

    const edges = new THREE.EdgesGeometry(indexed, 24);
    const edgeLines = new THREE.LineSegments(edges, new THREE.LineBasicMaterial({ color: new THREE.Color(this.colors.edge) }));
    this.model.add(edgeLines);

    indexed.dispose();
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
    if (this.hover?.kind === 'edge') {
      const [a, b] = this.edgeSegment(this.hover.edge, this.hover.side);
      this.overlay.add(this.fatLines([a.x, a.y, a.z, b.x, b.y, b.z], this.fatMaterial(this.colors.sel, 4.5, { depthTest: false })));
    } else if (this.hover?.kind === 'face' && this.state && this.pickMode !== 'edge') {
      this.overlay.add(this.pieceHighlight(this.hover.pieceKind, this.hover.id, this.colors.sel, 0.28));
    }
    this.requestRender();
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

  private toScreen(p: V3): { x: number; y: number; depth: number } {
    const r = this.renderer.domElement.getBoundingClientRect();
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
    if (!this.state) return null;
    if (this.pickMode === 'edge') return this.pickEdge(x, y);
    return this.pickFace(x, y);
  }

  private pickEdge(x: number, y: number): EdgeHit | null {
    const candidates: { d: number; hit: EdgeHit; p: V3 }[] = [];
    for (const e of this.pickables) {
      if (!this.edgeFilter(e)) continue;
      for (const side of ['top', 'bottom'] as const) {
        const [a, b] = this.edgeSegment(e, side);
        const sa = this.toScreen(a);
        const sb = this.toScreen(b);
        const dx = sb.x - sa.x;
        const dy = sb.y - sa.y;
        const l2 = dx * dx + dy * dy;
        let t = l2 > 0 ? ((x - sa.x) * dx + (y - sa.y) * dy) / l2 : 0;
        t = Math.max(0, Math.min(1, t));
        const d = Math.hypot(x - (sa.x + dx * t), y - (sa.y + dy * t));
        if (d > 11) continue;
        const p = v3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        candidates.push({ d, hit: { kind: 'edge', edge: e, side }, p });
      }
    }
    candidates.sort((u, w) => u.d - w.d);
    const tol = Math.max(0.5, 0.75 * (this.state?.part.T ?? 1));
    for (const c of candidates.slice(0, 12)) {
      if (!this.occluded(c.p, tol)) return c.hit;
    }
    return null;
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
    const out: FaceHit = { kind: 'face', pieceKind: piece.kind, id: piece.id, feature: piece.feature };
    if (piece.kind === 'face') {
      const M: Affine | undefined = flat ? part.anchor.M : mesh.placements.get(piece.id);
      if (M) {
        const local = applyA(invertRigid(M), v3(hit.point.x, hit.point.y, hit.point.z));
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
  if (a.kind === 'edge' && b.kind === 'edge') return a.edge === b.edge && a.side === b.side;
  if (a.kind === 'face' && b.kind === 'face') return a.id === b.id && a.pieceKind === b.pieceKind;
  return false;
}
