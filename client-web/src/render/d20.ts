import * as THREE from "three";

export const D20_FACES = 20;
const ATLAS_COLUMNS = 5;
const ATLAS_ROWS = 4;
const ATLAS_CELL_PIXELS = 128;
/** Triangle corners inside an atlas cell, in cell-relative UV; the first corner is the "top" of the number. */
const CELL_TRIANGLE: Array<[number, number]> = [[0.5, 0.95], [0.05, 0.12], [0.95, 0.12]];

export interface DieStyle {
  body: string;
  edge: string;
  ink: string;
}

export interface D20 {
  mesh: THREE.Mesh;
  /** Orientation that shows the given face (1..20) upright towards +Z, where the camera sits. */
  restingOrientation(face: number): THREE.Quaternion;
}

/** Builds a twenty-sided die whose face i (0-based) carries the number i + 1. */
export function createD20(style: DieStyle): D20 {
  const geometry = new THREE.IcosahedronGeometry(1, 0).toNonIndexed();
  const positions = geometry.getAttribute("position");
  const uvs = new Float32Array(positions.count * 2);
  const faceNormals: THREE.Vector3[] = [];
  const faceUps: THREE.Vector3[] = [];
  for (let face = 0; face < D20_FACES; face++) {
    const corners = [0, 1, 2].map((corner) => new THREE.Vector3().fromBufferAttribute(positions, face * 3 + corner));
    const centroid = corners[0].clone().add(corners[1]).add(corners[2]).divideScalar(3);
    faceNormals.push(centroid.clone().normalize());
    faceUps.push(corners[0].clone().sub(centroid).normalize());
    const { u0, v0 } = cellOrigin(face);
    CELL_TRIANGLE.forEach(([u, v], corner) => {
      uvs[(face * 3 + corner) * 2] = u0 + u / ATLAS_COLUMNS;
      uvs[(face * 3 + corner) * 2 + 1] = v0 + v / ATLAS_ROWS;
    });
  }
  geometry.setAttribute("uv", new THREE.BufferAttribute(uvs, 2));
  geometry.computeVertexNormals();
  const material = new THREE.MeshStandardMaterial({ map: atlasTexture(style), flatShading: true, roughness: 0.35, metalness: 0.15 });
  const mesh = new THREE.Mesh(geometry, material);

  return {
    mesh,
    restingOrientation(faceNumber: number): THREE.Quaternion {
      const index = THREE.MathUtils.clamp(Math.round(faceNumber), 1, D20_FACES) - 1;
      const normal = faceNormals[index];
      const up = faceUps[index].clone().sub(normal.clone().multiplyScalar(faceUps[index].dot(normal))).normalize();
      const right = new THREE.Vector3().crossVectors(up, normal).normalize();
      const local = new THREE.Matrix4().makeBasis(right, up, normal);
      return new THREE.Quaternion().setFromRotationMatrix(local.transpose());
    },
  };
}

function cellOrigin(face: number): { u0: number; v0: number } {
  const column = face % ATLAS_COLUMNS;
  const row = Math.floor(face / ATLAS_COLUMNS);
  return { u0: column / ATLAS_COLUMNS, v0: 1 - (row + 1) / ATLAS_ROWS };
}

function atlasTexture(style: DieStyle): THREE.CanvasTexture {
  const width = ATLAS_COLUMNS * ATLAS_CELL_PIXELS;
  const height = ATLAS_ROWS * ATLAS_CELL_PIXELS;
  const canvas = document.createElement("canvas");
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext("2d")!;
  context.fillStyle = style.body;
  context.fillRect(0, 0, width, height);
  for (let face = 0; face < D20_FACES; face++) {
    const { u0, v0 } = cellOrigin(face);
    const points = CELL_TRIANGLE.map(([u, v]) => ({
      x: (u0 + u / ATLAS_COLUMNS) * width,
      y: (1 - (v0 + v / ATLAS_ROWS)) * height,
    }));
    context.beginPath();
    points.forEach((point, index) => (index === 0 ? context.moveTo(point.x, point.y) : context.lineTo(point.x, point.y)));
    context.closePath();
    context.fillStyle = style.body;
    context.fill();
    context.lineWidth = 5;
    context.strokeStyle = style.edge;
    context.stroke();
    const centerX = (points[0].x + points[1].x + points[2].x) / 3;
    const centerY = (points[0].y + points[1].y + points[2].y) / 3;
    const label = String(face + 1);
    context.fillStyle = style.ink;
    context.font = `bold ${label.length > 1 ? 40 : 46}px "Trebuchet MS", sans-serif`;
    context.textAlign = "center";
    context.textBaseline = "middle";
    context.fillText(label, centerX, centerY + 6);
    if (label === "6" || label === "9") {
      context.fillRect(centerX - 10, centerY + 30, 20, 4);
    }
  }
  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  texture.anisotropy = 4;
  return texture;
}
