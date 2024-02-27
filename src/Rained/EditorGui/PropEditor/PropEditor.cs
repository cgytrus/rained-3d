using ImGuiNET;
using RainEd.Props;
using Raylib_cs;
using rlImGui_cs;
using System.Numerics;
namespace RainEd;

partial class PropEditor : IEditorMode
{
    public string Name { get => "Props"; }
    private readonly EditorWindow window;
    private int selectedGroup = 0;
    private int currentSelectorMode = 0;
    private PropInit? selectedInit = null;
    private readonly List<Prop> selectedProps = new();
    private List<Prop>? initSelectedProps = null; // used for add rect select mode
    private Vector2 prevMousePos;
    private Vector2 dragStartPos;

    private readonly Color HighlightColor = new(0, 0, 255, 255);
    private readonly Color HighlightColorGlow = new(50, 50, 255, 255);
    private readonly Color HighlightColor2 = new(180, 180, 180, 255);
    private readonly Color HighlightColor2Glow = new(255, 255, 255, 255);

    private bool isMouseDragging = false;
    private enum DragMode
    {
        Select,
        Move
    };
    private DragMode dragMode;

    private ITransformMode? transformMode;

    public PropEditor(EditorWindow window)
    {
        this.window = window;
    }

    public void Load()
    {
        selectedProps.Clear();
        initSelectedProps = null;
        isMouseDragging = false;
    }

    public void DrawToolbar()
    {
        var propDb = RainEd.Instance.PropDatabase;

        if (ImGui.Begin("Props", ImGuiWindowFlags.NoFocusOnAppearing))
        {
            // work layer
            {
                int workLayerV = window.WorkLayer + 1;
                ImGui.SetNextItemWidth(ImGui.GetTextLineHeightWithSpacing() * 4f);
                ImGui.InputInt("View Layer", ref workLayerV);
                window.WorkLayer = Math.Clamp(workLayerV, 1, 3) - 1;
            }
            
            if (ImGui.BeginTabBar("PropSelector"))
            {
                var halfWidth = ImGui.GetContentRegionAvail().X / 2f - ImGui.GetStyle().ItemSpacing.X / 2f;
                var boxHeight = ImGui.GetContentRegionAvail().Y;

                if (ImGui.BeginTabItem("Props"))
                {
                    // if tab changed, reset selected group back to 0
                    if (currentSelectorMode != 0)
                    {
                        currentSelectorMode = 0;
                        selectedGroup = 0;
                    }

                    // group list box
                    if (ImGui.BeginListBox("##Groups", new Vector2(halfWidth, boxHeight)))
                    {
                        for (int i = 0; i < propDb.Categories.Count; i++)
                        {
                            var group = propDb.Categories[i];
                            if (group.IsTileCategory) continue; // skip Tiles as props categories

                            if (ImGui.Selectable(group.Name, selectedGroup == i))
                                selectedGroup = i;
                        }
                        
                        ImGui.EndListBox();
                    }
                    
                    // group listing (effects) list box
                    ImGui.SameLine();
                    if (ImGui.BeginListBox("##Props", new Vector2(halfWidth, boxHeight)))
                    {
                        var propList = propDb.Categories[selectedGroup].Props;

                        for (int i = 0; i < propList.Count; i++)
                        {
                            var prop = propList[i];

                            if (ImGui.Selectable(prop.Name, prop == selectedInit))
                            {
                                selectedInit = prop;
                            }

                            if (ImGui.BeginItemTooltip())
                            {
                                var previewRect = prop.GetPreviewRectangle(0, prop.LayerCount / 2);
                                rlImGui.ImageRect(
                                    prop.Texture,
                                    (int)previewRect.Width, (int)previewRect.Height,
                                    previewRect
                                );
                                ImGui.EndTooltip();
                            }
                        }
                        
                        ImGui.EndListBox();
                    }

                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Tiles"))
                {
                    // if tab changed, reset selected group back to 0
                    if (currentSelectorMode != 1)
                    {
                        currentSelectorMode = 1;
                        selectedGroup = 0;
                    }

                    // group list box
                    if (ImGui.BeginListBox("##Groups", new Vector2(halfWidth, boxHeight)))
                    {
                        for (int i = 0; i < propDb.TileCategories.Count; i++)
                        {
                            if (ImGui.Selectable(propDb.TileCategories[i].Name, selectedGroup == i))
                                selectedGroup = i;
                        }
                        
                        ImGui.EndListBox();
                    }
                    
                    // group listing (effects) list box
                    ImGui.SameLine();
                    if (ImGui.BeginListBox("##Props", new Vector2(halfWidth, boxHeight)))
                    {
                        var propList = propDb.TileCategories[selectedGroup].Props;

                        for (int i = 0; i < propList.Count; i++)
                        {
                            var prop = propList[i];

                            if (ImGui.Selectable(prop.Name, prop == selectedInit))
                            {
                                selectedInit = prop;
                            }

                            if (ImGui.BeginItemTooltip())
                            {
                                var previewRect = prop.GetPreviewRectangle(0, prop.LayerCount / 2);
                                rlImGui.ImageRect(
                                    prop.Texture,
                                    (int)previewRect.Width, (int)previewRect.Height,
                                    previewRect
                                );
                                ImGui.EndTooltip();
                            }
                        }
                        
                        ImGui.EndListBox();
                    }

                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
            
        } ImGui.End();

        if (ImGui.Begin("Prop Options", ImGuiWindowFlags.NoFocusOnAppearing))
        {
            // prop transformation mode
            if (selectedProps.Count > 0)
            {
                if (selectedProps.Count == 1)
                {
                    ImGui.TextUnformatted($"Selected {selectedProps[0].PropInit.Name}");
                }
                else
                {
                    ImGui.Text("Selected multiple props");
                }

                // convert to/from freeform prop
                bool canConvert = false;

                foreach (var prop in selectedProps)
                {
                    if (prop.IsAffine)
                    {
                        canConvert = true;
                        break;
                    }
                }

                if (!canConvert)
                    ImGui.BeginDisabled();
                
                if (ImGui.Button("Convert to Warpable Prop"))
                {
                    foreach (var prop in selectedProps)
                        prop.ConvertToFreeform();
                }

                if (!canConvert)
                    ImGui.EndDisabled();
                
                int propDepthV = selectedProps[0].Depth;

                for (int i = 1; i < selectedProps.Count; i++)
                {
                    if (selectedProps[i].Depth != propDepthV)
                    {
                        propDepthV = int.MaxValue;
                    }
                }

                // depth
                ImGui.SetNextItemWidth(ImGui.GetTextLineHeightWithSpacing() * 4f);
                if (propDepthV != int.MaxValue)
                {
                    if (ImGui.InputInt("Depth", ref propDepthV))
                    {
                        propDepthV = Math.Clamp(propDepthV, 0, 29);
                        foreach (var prop in selectedProps)
                            prop.Depth = propDepthV;
                    }
                }

                if (selectedProps.Count == 1)
                {
                    var prop = selectedProps[0];

                    // prop settings

                    // notes + synopses
                    ImGui.SeparatorText("Notes");

                    if (prop.PropInit.PropFlags.HasFlag(PropFlags.Tile))
                        ImGui.BulletText("Tile as Prop");

                    if (prop.PropInit.PropFlags.HasFlag(PropFlags.ProcedurallyShaded))
                        ImGui.BulletText("Procedurally Shaded");

                    if (prop.PropInit.PropFlags.HasFlag(PropFlags.RandomVariations))
                        ImGui.BulletText("Random Variation");
                    
                    if (prop.PropInit.PropFlags.HasFlag(PropFlags.HasVariations))
                        ImGui.BulletText("Variation Selectable");
                    
                    if (prop.PropInit.PropFlags.HasFlag(PropFlags.PostEffectsWhenColorized))
                        ImGui.BulletText("Post Effects Recommended When Colored");

                    if (prop.PropInit.PropFlags.HasFlag(PropFlags.CanColorTube))
                        ImGui.BulletText("Can Color Tube");
                    
                    if (prop.PropInit.PropFlags.HasFlag(PropFlags.CanSetThickness))
                        ImGui.BulletText("Can Set Thickness");

                    if (prop.PropInit.PropFlags.HasFlag(PropFlags.CustomColorAvailable))
                        ImGui.BulletText("Custom Color Available");
                }
            }
            else
            {
                ImGui.Text("No props selected");
            }

        } ImGui.End();
    }

    private static bool IsPointInTriangle(Vector2 pt, Vector2 v1, Vector2 v2, Vector2 v3)
    {
        static float sign(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);
        }

        float d1 = sign(pt, v1, v2);
        float d2 = sign(pt, v2, v3);
        float d3 = sign(pt, v3, v1);

        bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
        return !(hasNeg && hasPos);
    }

    private static Prop? GetPropAt(Vector2 point)
    {
        foreach (var prop in RainEd.Instance.Level.Props)
        {
            var pts = prop.QuadPoints;
            if (
                IsPointInTriangle(point, pts[0], pts[1], pts[2]) ||
                IsPointInTriangle(point, pts[2], pts[3], pts[0])    
            )
            {
                return prop;
            }
        }

        return null;
    }

    /*private static Rectangle GetPropAABB(Prop prop)
    {
        var minX = Math.Min(prop.Quad[0].X, Math.Min(prop.Quad[1].X, Math.Min(prop.Quad[2].X, prop.Quad[3].X)));
        var minY = Math.Min(prop.Quad[0].Y, Math.Min(prop.Quad[1].Y, Math.Min(prop.Quad[2].Y, prop.Quad[3].Y)));
        var maxX = Math.Max(prop.Quad[0].X, Math.Min(prop.Quad[1].X, Math.Min(prop.Quad[2].X, prop.Quad[3].X)));
        var maxY = Math.Max(prop.Quad[0].Y, Math.Min(prop.Quad[1].Y, Math.Min(prop.Quad[2].Y, prop.Quad[3].Y)));
        return new Rectangle(minX, minY, maxX - minX, maxY - minY);
    }*/
    private Rectangle GetSelectionAABB()
        => CalcPropExtents(selectedProps);

    private static Rectangle CalcPropExtents(List<Prop> props)
    {
        var minX = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var minY = float.PositiveInfinity;
        var maxY = float.NegativeInfinity;

        foreach (var prop in props)
        {
            for (int i = 0; i < 4; i++)
            {
                var pts = prop.QuadPoints;
                minX = Math.Min(minX, pts[i].X);
                minY = Math.Min(minY, pts[i].Y);
                maxX = Math.Max(maxX, pts[i].X);
                maxY = Math.Max(maxY, pts[i].Y);
            }
        }

        return new Rectangle(minX, minY, maxX - minX, maxY - minY);
    }

    private static Vector2 GetPropCenter(Prop prop)
    {
        if (prop.IsAffine)
            return prop.Rect.Center;
        
        var pts = prop.QuadPoints;
        return (pts[0] + pts[1] + pts[2] + pts[3]) / 4f;
    }

    // returns true if gizmo is hovered, false if not
    private bool DrawGizmoHandle(Vector2 pos, bool secondaryColor = false)
    {
        bool isGizmoHovered = window.IsViewportHovered && (window.MouseCellFloat - pos).Length() < 0.5f / window.ViewZoom;
        
        Color color;
        if (secondaryColor)
        {
            color = isGizmoHovered ? HighlightColor2Glow : HighlightColor2;
        }
        else
        {
            color = isGizmoHovered ? HighlightColorGlow : HighlightColor;
        }

        Raylib.DrawCircleV(
            pos * Level.TileSize,
            (isGizmoHovered ? 8f : 4f) / window.ViewZoom,
            color
        );

        return isGizmoHovered;
    }

    public void DrawViewport(RlManaged.RenderTexture2D mainFrame, RlManaged.RenderTexture2D layerFrame)
    {
        bool wasMouseDragging = isMouseDragging;
        isMouseDragging = false;

        var level = window.Editor.Level;
        var levelRender = window.LevelRenderer;

        // draw level background (solid white)
        Raylib.DrawRectangle(0, 0, level.Width * Level.TileSize, level.Height * Level.TileSize, new Color(127, 127, 127, 255));

        // draw layers
        for (int l = Level.LayerCount-1; l >= 0; l--)
        {
            // draw layer into framebuffer
            Raylib.BeginTextureMode(layerFrame);

            Raylib.ClearBackground(new Color(0, 0, 0, 0));
            levelRender.RenderGeometry(l, new Color(0, 0, 0, 255));
            levelRender.RenderTiles(l, 255);
            levelRender.RenderProps(l, 255);
            
            // draw alpha-blended result into main frame
            Raylib.BeginTextureMode(mainFrame);
            Rlgl.PushMatrix();
            Rlgl.LoadIdentity();

            int offset = l * 2;
            var alpha = l == window.WorkLayer ? 255 : 50;
            Raylib.DrawTextureRec(
                layerFrame.Texture,
                new Rectangle(0f, layerFrame.Texture.Height, layerFrame.Texture.Width, -layerFrame.Texture.Height),
                Vector2.One * offset,
                new Color(255, 255, 255, alpha)
            );
            Rlgl.PopMatrix();
        }

        levelRender.RenderGrid();
        levelRender.RenderBorder();

        // highlight selected props
        foreach (var prop in selectedProps)
        {
            var pts = prop.QuadPoints;
            var col = prop.IsAffine ? HighlightColor : HighlightColor2;
            Raylib.DrawLineEx(pts[0] * Level.TileSize, pts[1] * Level.TileSize, 1f / window.ViewZoom, col);
            Raylib.DrawLineEx(pts[1] * Level.TileSize, pts[2] * Level.TileSize, 1f / window.ViewZoom, col);
            Raylib.DrawLineEx(pts[2] * Level.TileSize, pts[3] * Level.TileSize, 1f / window.ViewZoom, col);
            Raylib.DrawLineEx(pts[3] * Level.TileSize, pts[0] * Level.TileSize, 1f / window.ViewZoom, col);
        }

        // prop transform gizmos
        if (selectedProps.Count > 0)
        {
            bool canWarp = transformMode is WarpTransformMode ||
                (ImGui.IsKeyDown(ImGuiKey.F) && selectedProps.Count == 1 && !selectedProps[0].IsAffine);
            
            var aabb = GetSelectionAABB();

            // draw selection AABB if there is more than
            // one prop selected
            if (!canWarp && (selectedProps.Count > 1 || !selectedProps[0].IsAffine))
            {
                Raylib.DrawRectangleLinesEx(
                    new Rectangle(
                        aabb.Position * Level.TileSize,
                        aabb.Size * Level.TileSize
                    ),
                    1f / window.ViewZoom,
                    HighlightColor
                );
            }
            
            // scale gizmo (points on corners/edges)
            // don't draw handles if rotating
            if ((transformMode is null && !canWarp) || transformMode is ScaleTransformMode)
            {
                ScaleTransformMode? scaleMode = transformMode as ScaleTransformMode;

                Vector2[] corners;

                if (selectedProps.Count == 1 && selectedProps[0].IsAffine)
                {
                    corners = selectedProps[0].QuadPoints;
                }
                else
                {
                    corners = new Vector2[4]
                    {
                        aabb.Position + aabb.Size * new Vector2(0f, 0f),
                        aabb.Position + aabb.Size * new Vector2(1f, 0f),
                        aabb.Position + aabb.Size * new Vector2(1f, 1f),
                        aabb.Position + aabb.Size * new Vector2(0f, 1f),
                    };
                };

                // even i's are corner points
                // odd i's are edge points
                for (int i = 0; i < 8; i++)
                {
                    // don't draw this handle if another scale handle is active
                    if (scaleMode != null && scaleMode.handleId != i)
                    {
                        continue;
                    }
                    
                    var handle1 = corners[i / 2]; // position of left corner
                    var handle2 = corners[(i + 1) / 2 % 4]; // position of right corner
                    var handlePos = (handle1 + handle2) / 2f;
                    
                    // draw gizmo handle at corner
                    if (DrawGizmoHandle(handlePos) && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        transformMode = new ScaleTransformMode(
                            handleId: i,
                            props: selectedProps
                        );
                    }
                }
            }

            // rotation gizmo (don't draw if scaling or rotating) 
            if (transformMode is null && !canWarp)
            {
                Vector2 sideDir = Vector2.UnitX;
                Vector2 handleDir = -Vector2.UnitY;
                Vector2 handleCnPos = aabb.Position + new Vector2(aabb.Width / 2f, 0f);

                if (selectedProps.Count == 1 && selectedProps[0].IsAffine)
                {
                    handleCnPos = (selectedProps[0].QuadPoints[0] + selectedProps[0].QuadPoints[1]) / 2f;
                    sideDir = Vector2.Normalize(selectedProps[0].QuadPoints[1] - selectedProps[0].QuadPoints[0]);
                    handleDir = new(sideDir.Y, -sideDir.X);
                }

                Vector2 rotDotPos = handleCnPos + handleDir * 5f;

                // draw line to gizmo handle
                Raylib.DrawLineEx(
                    startPos: handleCnPos * Level.TileSize,
                    endPos: rotDotPos * Level.TileSize,
                    1f / window.ViewZoom,
                    HighlightColor
                );

                // draw gizmo handle
                if (DrawGizmoHandle(rotDotPos) && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    transformMode = new RotateTransformMode(
                        rotCenter: aabb.Position + aabb.Size / 2f,
                        props: selectedProps
                    );
                }
            }

            // freeform warp gizmo
            if ((transformMode is null && canWarp) || transformMode is WarpTransformMode)
            {
                var warpMode = transformMode as WarpTransformMode;
                Vector2[] corners = selectedProps[0].QuadPoints;
                
                for (int i = 0; i < 4; i++)
                {
                    // don't draw this handle if another scale handle is active
                    if (warpMode != null && warpMode.handleId != i)
                    {
                        continue;
                    }

                    var handlePos = corners[i];
                    
                    // draw gizmo handle at corner
                    if (DrawGizmoHandle(handlePos, true) && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        transformMode = new WarpTransformMode(
                            handleId: i,
                            prop: selectedProps[0]
                        );
                    }
                }
            }
        }

        // draw drag rect
        if (wasMouseDragging && dragMode == DragMode.Select)
        {
            var minX = Math.Min(dragStartPos.X, window.MouseCellFloat.X);
            var maxX = Math.Max(dragStartPos.X, window.MouseCellFloat.X);
            var minY = Math.Min(dragStartPos.Y, window.MouseCellFloat.Y);
            var maxY = Math.Max(dragStartPos.Y, window.MouseCellFloat.Y);

            var rect = new Rectangle(
                minX * Level.TileSize,
                minY * Level.TileSize,
                (maxX - minX) * Level.TileSize,
                (maxY - minY) * Level.TileSize
            );
            Raylib.DrawRectangleRec(rect, new Color(HighlightColor.R, HighlightColor.G, HighlightColor.B, (byte)80));
            Raylib.DrawRectangleLinesEx(rect, 1f / window.ViewZoom, HighlightColor);

            // select all props within selection rectangle
            selectedProps.Clear();
            
            if (initSelectedProps is not null)
            {
                foreach (var prop in initSelectedProps)
                    selectedProps.Add(prop);
            }

            foreach (var prop in level.Props)
            {
                var pc = GetPropCenter(prop);
                if (pc.X >= minX && pc.Y >= minY && pc.X <= maxX && pc.Y <= maxY)
                    selectedProps.Add(prop);
            }
        }

        if (window.IsViewportHovered)
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                dragStartPos = window.MouseCellFloat;
            }

            // in prop transform mode
            if (transformMode is not null)
            {
                transformMode.Update(dragStartPos, window.MouseCellFloat);
                
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                {
                    transformMode = null;
                }
            }
            else
            {
                // in default mode
                PropSelectUpdate(wasMouseDragging);
            }
        }

        prevMousePos = window.MouseCellFloat;
    }

    public void PropSelectUpdate(bool wasMouseDragging)
    {
        if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            if (!wasMouseDragging)
            {
                // drag had begun
                var hoverProp = GetPropAt(dragStartPos);

                // if dragging over an empty space, begin rect select
                if (hoverProp is null)
                {
                    dragMode = DragMode.Select;

                    // if shift is held, rect select Adds instead of Replace
                    if (ImGui.IsKeyDown(ImGuiKey.ModShift))
                        initSelectedProps = selectedProps.ToList(); // clone selection list
                    else
                        initSelectedProps = null;
                }
                else
                {
                    // if draggging over a prop, drag all currently selected props
                    // if active prop is in selection. if not, then set selection
                    // to this prop
                    dragMode = DragMode.Move;
                    if (!selectedProps.Contains(hoverProp))
                    {
                        selectedProps.Clear();
                        selectedProps.Add(hoverProp);
                    }
                }
            }
            isMouseDragging = true;

            // move drag
            if (dragMode == DragMode.Move)
            {
                var mouseDelta = window.MouseCellFloat - prevMousePos;
                foreach (var prop in selectedProps)
                {
                    if (prop.IsAffine)
                        prop.Rect.Center += mouseDelta;
                    else
                    {
                        var pts = prop.QuadPoints;
                        pts[0] += mouseDelta;
                        pts[1] += mouseDelta;
                        pts[2] += mouseDelta;
                        pts[3] += mouseDelta;
                    }
                }
            }
        }

        // user clicked a prop, so add it to the selection
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && !wasMouseDragging)
        {
            if (!ImGui.IsKeyDown(ImGuiKey.ModShift))
                selectedProps.Clear();
            
            var prop = GetPropAt(window.MouseCellFloat);
            if (prop is not null)
            {
                if (ImGui.IsKeyDown(ImGuiKey.ModShift))
                {
                    // if prop is in selection, remove it from selection
                    // if prop is not in selection, add it to the selection
                    if (!selectedProps.Remove(prop))
                        selectedProps.Add(prop);
                }
                else
                {
                    selectedProps.Add(prop);
                }
            }
        }

        // when N is pressed, create new selected prop
        // TODO: drag and drop from props list
        if (RainEd.Instance.IsShortcutActivated("NewObject") || ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            if (selectedInit is not null)
            {
                var prop = new Prop(selectedInit, window.MouseCellFloat, new Vector2(selectedInit.Width, selectedInit.Height))
                {
                    Depth = window.WorkLayer * 10
                };

                RainEd.Instance.Level.Props.Add(prop);
                selectedProps.Clear();
                selectedProps.Add(prop);
            }
        }

        // delete key to delete selected props
        if (ImGui.IsKeyPressed(ImGuiKey.Delete) || ImGui.IsKeyPressed(ImGuiKey.Backspace))
        {
            foreach (var prop in selectedProps)
            {
                RainEd.Instance.Level.Props.Remove(prop);
            }

            selectedProps.Clear();
        }

        // duplicate props
        if (ImGui.IsKeyPressed(ImGuiKey.D) && ImGui.IsKeyDown(ImGuiKey.ModCtrl))
        {
            var propsToDup = selectedProps.ToArray();
            selectedProps.Clear();

            foreach (var srcProp in propsToDup)
            {
                Prop newProp;
                if (srcProp.IsAffine)
                {
                    newProp = new Prop(srcProp.PropInit, srcProp.Rect.Center + Vector2.One, srcProp.Rect.Size);
                    newProp.Rect.Rotation = srcProp.Rect.Rotation;
                }
                else
                {
                    newProp = new Prop(srcProp.PropInit, srcProp.QuadPoints);
                    newProp.QuadPoints[0] += Vector2.One;
                    newProp.QuadPoints[1] += Vector2.One;
                    newProp.QuadPoints[2] += Vector2.One;
                    newProp.QuadPoints[3] += Vector2.One;
                }

                RainEd.Instance.Level.Props.Add(newProp);
                selectedProps.Add(newProp);
            }
        }
    }
}