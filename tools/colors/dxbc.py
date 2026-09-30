import UnityPy, os, ctypes, struct, sys
import lz4.block
UnityPy.config.FALLBACK_UNITY_VERSION = "6000.3.9f1"
root = r"C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2\GraveyardKeeper2_Data\StreamingAssets\aa\StandaloneWindows64"
out = os.path.dirname(os.path.abspath(__file__))
d3d = ctypes.WinDLL(r"C:\Windows\System32\D3DCompiler_47.dll")
D3DDisassemble = d3d.D3DDisassemble
D3DDisassemble.argtypes = [ctypes.c_void_p, ctypes.c_size_t, ctypes.c_uint, ctypes.c_char_p, ctypes.POINTER(ctypes.c_void_p)]


def disasm(buf):
    blob = ctypes.c_void_p()
    b = ctypes.create_string_buffer(buf, len(buf))
    hr = D3DDisassemble(b, len(buf), 0, None, ctypes.byref(blob))
    if hr != 0:
        return None
    vt = ctypes.cast(ctypes.cast(blob, ctypes.POINTER(ctypes.c_void_p))[0], ctypes.POINTER(ctypes.c_void_p))
    GetBufferPointer = ctypes.WINFUNCTYPE(ctypes.c_void_p, ctypes.c_void_p)(vt[3])
    GetBufferSize = ctypes.WINFUNCTYPE(ctypes.c_size_t, ctypes.c_void_p)(vt[4])
    Release = ctypes.WINFUNCTYPE(ctypes.c_ulong, ctypes.c_void_p)(vt[2])
    p = GetBufferPointer(blob); n = GetBufferSize(blob)
    s = ctypes.string_at(p, n).decode("utf-8", "replace")
    Release(blob)
    return s


for b, want in [("29c8a55e8c297a61989cebe54420bd5a.bundle", "Custom/3D Sprite"), ("a1fdb024b5209c946e2ec52ad1d11b45.bundle", "Custom/3D Object")]:
    env = UnityPy.load(os.path.join(root, b))
    for obj in env.objects:
        if obj.type.name != "Shader":
            continue
        d = obj.read_typetree()
        if d["m_ParsedForm"]["m_Name"] != want:
            continue
        blob = bytes(d["compressedBlob"])
        offs = d["offsets"][0]; cl = d["compressedLengths"][0]; dl = d["decompressedLengths"][0]
        parts = []
        for o, c, u in zip(offs, cl, dl):
            parts.append(lz4.block.decompress(blob[o:o + c], uncompressed_size=u))
        data = b"".join(parts)
        tag = want.replace("/", "_").replace(" ", "_")
        open(os.path.join(out, tag + ".bin"), "wb").write(data)
        i = 0; n = 0
        outd = os.path.join(out, tag); os.makedirs(outd, exist_ok=True)
        while True:
            i = data.find(b"DXBC", i)
            if i < 0:
                break
            size = struct.unpack_from("<I", data, i + 24)[0]
            chunk = data[i:i + size]
            s = disasm(chunk)
            if s:
                kind = "ps" if "\nps_" in s else ("vs" if "\nvs_" in s else "xx")
                open(os.path.join(outd, f"{n:04d}_{kind}.txt"), "w", encoding="utf-8").write(s)
                n += 1
            i += 4
        print(want, len(data), "programs", n)
