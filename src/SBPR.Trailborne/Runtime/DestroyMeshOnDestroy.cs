using UnityEngine;

namespace SBPR.Trailborne.Runtime
{
    // REUSABLE — feature-agnostic owned-Mesh janitor.
    //
    // A Mesh created at runtime via Object.Instantiate(someSharedMesh) is an unmanaged
    // asset: it is NOT destroyed when the GameObject that references it is destroyed, so
    // anything that bakes a per-instance mesh (e.g. the cairn banner's vertex-baked cloth
    // mesh) leaks one Mesh per rebuild unless someone Destroys it explicitly. Attach this
    // to the GameObject that owns the baked mesh and hand it the mesh; OnDestroy disposes
    // it on EVERY teardown path (tier rebuild, cairn removal, scene unload) — no leak,
    // no per-call bookkeeping at the call site.
    //
    // Only ever give it a mesh YOU created (Instantiate / new Mesh). Never a shared donor
    // asset — destroying a shared asset would corrupt every other user of it.
    public sealed class DestroyMeshOnDestroy : MonoBehaviour
    {
        public Mesh? Owned;

        private void OnDestroy()
        {
            if (Owned != null)
            {
                Object.Destroy(Owned);
                Owned = null;
            }
        }
    }
}
