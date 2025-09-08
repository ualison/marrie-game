using Godot;
using System;

public partial class FallingLeafRenderer : Node2D
{
    // Parent tree reference (set by OrganicFractalTree when creating this child)
    public OrganicFractalTree ParentTree { get; set; }

    public override void _Ready()
    {
    }

    public override void _Draw()
    {
        if (ParentTree == null) return;

        var infos = ParentTree.GetFallingLeafInfos();
        if (infos == null || infos.Length == 0) return;

    var tex = ParentTree.GetCachedLeafTexture();
    bool raster = ParentTree.IsLeafTextureCachedRaster();

    if (raster && tex != null)
        {
            Vector2 texSize = tex.GetSize();
            Vector2 texCenter = texSize * 0.5f;

            for (int i = 0; i < infos.Length; i++)
            {
                var info = infos[i];
                // convert from parent's local (tree) coordinates to this renderer's local coordinates
                Vector2 globalPos = ParentTree.ToGlobal(info.Position);
                Vector2 localPos = ToLocal(globalPos);
                // sample current parent leaf color each iteration so dynamic changes are respected
                Color baseLeafColor = ParentTree.GetLeafColor();
                DrawSetTransform(localPos, info.Rotation, Vector2.One * info.Scale);
                DrawTexture(tex, -texCenter, baseLeafColor);
                DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            }
        }
        else if (ParentTree.IsLeafTemplateCached())
        {
            float eff = ParentTree.GetEffectiveLeafSizePublic();
            for (int i = 0; i < infos.Length; i++)
            {
                var info = infos[i];
                Vector2 globalPos = ParentTree.ToGlobal(info.Position);
                Vector2 localPos = ToLocal(globalPos);
                // sample color here as well to match tree's leafColor
                Color baseLeafColor = ParentTree.GetLeafColor();
                DrawCircle(localPos, eff * 0.7f * info.Scale, baseLeafColor);
            }
        }
    }
}
