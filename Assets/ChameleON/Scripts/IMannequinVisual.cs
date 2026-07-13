using System.Collections.Generic;

public interface IMannequinVisual
{
    int PoseIndex { get; }
    IReadOnlyList<PaintableBodyPart> PaintableParts { get; }

    void Build();
    void ApplyPose(int index);
    string GetPoseName();
    void ClearPaint();
    void SetMaterialResponse(float metallic, float smoothness);
    void SetLocomotion(float normalizedSpeed);
}
