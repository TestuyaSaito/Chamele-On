// Cinemachine 3.1.x contains an editor-only asmref for HDRP even in URP projects.
// This empty assembly gives that asmref a valid target when HDRP is not installed.
// The assembly is disabled automatically if an actual HDRP package is added.
namespace ChameleON.PackageCompatibility
{
    internal static class HDRPEditorAssemblyStub
    {
    }
}
