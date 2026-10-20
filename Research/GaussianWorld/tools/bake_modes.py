"""Lowest vibration modes of the flower cage exported by Unity (GaussianModes.ExportCage).
Linear tetrahedral FEM with the learned per-tet Lame parameters (the small-strain limit of the
fixed-corotated "jelly" model PhysDreamer simulates) and lumped node masses. Anchored nodes are
eliminated. Modes are mass-normalised: phi^T M phi = I, K phi = omega^2 M phi.
Usage: bake_modes.py cage.bin modes.bin [mode_count]
"""
import hashlib, json, struct, sys
from pathlib import Path
import numpy as np
import scipy.sparse as sp
from scipy.sparse.linalg import eigsh

def read_cage(path):
    data = Path(path).read_bytes(); n, t = struct.unpack_from('<ii', data, 0); o = 8
    def take(dtype, count):
        nonlocal o; a = np.frombuffer(data, dtype=dtype, count=count, offset=o); o += a.nbytes; return a
    rest = take('<f4', n * 3).reshape(n, 3).astype(np.float64); inverse_mass = take('<f4', n).astype(np.float64)
    tets = take('<i4', t * 4).reshape(t, 4); mu = take('<f4', t).astype(np.float64); lam = take('<f4', t).astype(np.float64)
    return rest, inverse_mass, tets, mu, lam, hashlib.sha256(data).hexdigest()

def stiffness(rest, tets, mu, lam):
    x = rest[tets]; dm = np.stack([x[:, 1] - x[:, 0], x[:, 2] - x[:, 0], x[:, 3] - x[:, 0]], axis=2)
    volume = np.abs(np.linalg.det(dm)) / 6; g = np.linalg.inv(dm)            # F = Ds G, grad N_{j+1} = G[j, :]
    grads = np.concatenate([-g.sum(axis=1, keepdims=True), g], axis=1)        # (t, 4, 3)
    dot = np.einsum('tia,tja->tij', grads, grads)
    eye = np.eye(3)
    # K[ia, jb] = V (mu (gi.gj d_ab + g_ib g_ja) + lambda g_ia g_jb)
    ke = (mu[:, None, None, None, None] * (dot[:, :, None, :, None] * eye[None, None, :, None, :]
          + np.einsum('tib,tja->tiajb', grads, grads))
          + lam[:, None, None, None, None] * np.einsum('tia,tjb->tiajb', grads, grads)) * volume[:, None, None, None, None]
    dofs = (tets[:, :, None] * 3 + np.arange(3)[None, None, :]).reshape(len(tets), 12)
    rows = np.repeat(dofs, 12, axis=1).ravel(); cols = np.tile(dofs, (1, 12)).ravel()
    n = rest.shape[0] * 3
    return sp.csr_matrix((ke.reshape(len(tets), 12, 12).ravel(), (rows, cols)), shape=(n, n))

if __name__ == '__main__':
    cage, output = sys.argv[1], sys.argv[2]; count = int(sys.argv[3]) if len(sys.argv) > 3 else 48
    rest, inverse_mass, tets, mu, lam, digest = read_cage(cage)
    k = stiffness(rest, tets, mu, lam)
    free_nodes = np.flatnonzero(inverse_mass > 0)
    free = (free_nodes[:, None] * 3 + np.arange(3)).ravel()
    kf = k[free][:, free]; mass = np.repeat(1 / inverse_mass[free_nodes], 3)
    # Generalised problem with diagonal M -> symmetric standard problem in M^-1/2 coordinates.
    s = 1 / np.sqrt(mass); a = sp.diags(s) @ kf @ sp.diags(s)
    values, vectors = eigsh(a, k=count, sigma=0, which='LM')
    order = np.argsort(values); values = np.maximum(values[order], 0); vectors = vectors[:, order] * s[:, None]
    phi = np.zeros((rest.shape[0] * 3, count)); phi[free] = vectors
    omega = np.sqrt(values)
    with open(output, 'wb') as f:
        f.write(struct.pack('<ii', count, rest.shape[0])); f.write(omega.astype('<f4').tobytes())
        f.write(phi.reshape(rest.shape[0], 3, count).astype('<f4').tobytes())
    report = {'cage_sha256': digest, 'nodes': int(rest.shape[0]), 'free_nodes': int(len(free_nodes)), 'tets': int(len(tets)),
              'modes': count, 'frequencies_hz': (omega / (2 * np.pi)).round(3).tolist(),
              'orthonormality_error': float(np.abs(vectors.T @ (mass[:, None] * vectors) - np.eye(count)).max())}
    Path(output).with_suffix('.json').write_text(json.dumps(report, indent=2)); print(json.dumps({k: v for k, v in report.items() if k != 'frequencies_hz'} | {'lowest_hz': report['frequencies_hz'][:6], 'highest_hz': report['frequencies_hz'][-1]}))
