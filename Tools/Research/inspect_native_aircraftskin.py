"""Print the installed AircraftSkin DXBC for material-contract investigation (Windows)."""
import argparse
import ctypes

import UnityPy
from UnityPy.export.ShaderConverter import ShaderProgram
from UnityPy.helpers.CompressionHelper import decompress_lz4
from UnityPy.streams import EndianBinaryReader


def disassemble(code):
    offset = code.find(b"DXBC")
    if offset < 0:
        raise ValueError("Selected shader program is not DXBC")
    code = code[offset:]
    code = code[:int.from_bytes(code[24:28], "little")]
    library = ctypes.WinDLL("d3dcompiler_47.dll")
    function = library.D3DDisassemble
    function.argtypes = [ctypes.c_void_p, ctypes.c_size_t, ctypes.c_uint,
                         ctypes.c_char_p, ctypes.POINTER(ctypes.c_void_p)]
    function.restype = ctypes.c_long
    blob = ctypes.c_void_p()
    buffer = ctypes.create_string_buffer(code)
    result = function(buffer, len(code), 0, None, ctypes.byref(blob))
    if result < 0:
        raise RuntimeError(f"D3DDisassemble failed: {result:#x}")
    table = ctypes.cast(blob, ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p))).contents
    pointer = ctypes.WINFUNCTYPE(ctypes.c_void_p, ctypes.c_void_p)(table[3])
    size = ctypes.WINFUNCTYPE(ctypes.c_size_t, ctypes.c_void_p)(table[4])
    release = ctypes.WINFUNCTYPE(ctypes.c_ulong, ctypes.c_void_p)(table[2])
    try:
        return ctypes.string_at(pointer(blob), size(blob)).decode("utf-8").rstrip("\0")
    finally:
        release(blob)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("resources", help="Installed NuclearOption_Data/resources.assets")
    parser.add_argument("--program", type=int, default=256)
    args = parser.parse_args()
    env = UnityPy.load(args.resources)
    shader = next(shader for obj in env.objects if obj.type.name == "Shader"
                  for shader in [obj.read()] if shader.m_ParsedForm.m_Name == "Shader Graphs/AircraftSkin")
    platform = list(shader.platforms).index(4)
    start = shader.offsets[platform][0]
    length = shader.compressedLengths[platform][0]
    raw = decompress_lz4(bytes(shader.compressedBlob)[start:start + length],
                        shader.decompressedLengths[platform][0])
    programs = ShaderProgram(EndianBinaryReader(raw, endian="<"), shader.object_reader.version)
    print(disassemble(bytes(programs.m_SubPrograms[args.program].m_ProgramCode)))


if __name__ == "__main__":
    main()
