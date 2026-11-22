// Minimal GLB reader for checking Meshy output offline (no WebGL): JSON chunk, binary chunk and accessors.
import { readFileSync } from "node:fs";

const GLB_HEADER_BYTES = 12;
const CHUNK_HEADER_BYTES = 8;
const COMPONENT_ARRAYS = { 5120: Int8Array, 5121: Uint8Array, 5122: Int16Array, 5123: Uint16Array, 5125: Uint32Array, 5126: Float32Array };
const TYPE_SIZES = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4, MAT4: 16 };

export function readGlb(path) {
  const buffer = readFileSync(path);
  const jsonLength = buffer.readUInt32LE(GLB_HEADER_BYTES);
  const jsonStart = GLB_HEADER_BYTES + CHUNK_HEADER_BYTES;
  const json = JSON.parse(buffer.subarray(jsonStart, jsonStart + jsonLength).toString("utf8"));
  const binStart = jsonStart + jsonLength + CHUNK_HEADER_BYTES;
  const bin = buffer.subarray(binStart);
  return { json, bin, accessor: (index) => readAccessor(json, bin, index) };
}

/** Returns a typed array copy of a tightly packed or interleaved accessor. */
function readAccessor(json, bin, index) {
  const accessor = json.accessors[index];
  const view = json.bufferViews[accessor.bufferView];
  const ArrayType = COMPONENT_ARRAYS[accessor.componentType];
  const width = TYPE_SIZES[accessor.type];
  const elementBytes = ArrayType.BYTES_PER_ELEMENT * width;
  const stride = view.byteStride ?? elementBytes;
  const offset = (view.byteOffset ?? 0) + (accessor.byteOffset ?? 0);
  const result = new ArrayType(accessor.count * width);
  for (let element = 0; element < accessor.count; element++) {
    const source = new ArrayType(bin.buffer.slice(bin.byteOffset + offset + element * stride, bin.byteOffset + offset + element * stride + elementBytes));
    result.set(source, element * width);
  }
  return result;
}

/** UVs and positions of the first primitive, the only one Meshy characters have. */
export function firstPrimitive(glb) {
  const primitive = glb.json.meshes[0].primitives[0];
  return {
    positions: glb.accessor(primitive.attributes.POSITION),
    uvs: glb.accessor(primitive.attributes.TEXCOORD_0),
    joints: primitive.attributes.JOINTS_0 === undefined ? null : glb.accessor(primitive.attributes.JOINTS_0),
    weights: primitive.attributes.WEIGHTS_0 === undefined ? null : glb.accessor(primitive.attributes.WEIGHTS_0),
  };
}
