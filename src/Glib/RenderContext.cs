namespace Glib;

using Silk.NET.Windowing;
using Silk.NET.OpenGL;
using System.Numerics;

public class RenderContext : IDisposable
{
    private readonly GL gl;
    private bool _disposed = false;

    // batch variables
    private const uint VertexDataSize = 9;
    private const uint MaxVertices = 1024;

    private readonly float[] batchData;
    private uint numVertices = 0;
    private readonly uint batchBuffer;
    private readonly uint batchVertexArray;

    private readonly Shader defaultShader;
    private Shader? shaderValue;

    private readonly Stack<Matrix4x4> transformStack = [];
    internal Matrix4x4 BaseTransform = Matrix4x4.Identity;
    public Matrix4x4 TransformMatrix;
    public Color BackgroundColor = Color.Black;
    public Color DrawColor = Color.White;
    public float LineWidth = 1.0f;
    private Vector2 UV = Vector2.Zero;
    public Shader? Shader {
        get => shaderValue;
        set
        {
            if (shaderValue == value) return;
            DrawBatch();
            shaderValue = value;
        }
    }

    internal unsafe RenderContext(IWindow window)
    {
        gl = GL.GetApi(window);
        //gl.Disable(EnableCap.CullFace);
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        // create default shader
        defaultShader = new Shader(gl);

        batchData = new float[MaxVertices * VertexDataSize];

        // create batch buffer
        batchVertexArray = gl.CreateVertexArray();
        batchBuffer = gl.CreateBuffer();
        gl.BindVertexArray(batchVertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, batchBuffer);
        gl.BufferData(BufferTargetARB.ArrayBuffer, MaxVertices * VertexDataSize*sizeof(float), null, BufferUsageARB.StreamDraw);
        
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, VertexDataSize*sizeof(float), 0); // vertices
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, VertexDataSize*sizeof(float), 3*sizeof(float)); // uvs
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(2, 4, GLEnum.Float, false, VertexDataSize*sizeof(float), 5*sizeof(float)); // colors
        gl.EnableVertexAttribArray(2);

        TransformMatrix = Matrix4x4.Identity;
    }

    /*~Graphics()
    {
        Dispose(false);
    }*/

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                defaultShader.Dispose();
            }

            gl.DeleteBuffer(batchBuffer);
            gl.DeleteVertexArray(batchVertexArray);

            _disposed = true;
        }
    }

    internal void Resize(uint w, uint h)
    {
        gl.Viewport(0, 0, w, h);
    }

    public void ClearBackground(Color color)
    {
        gl.ClearColor(color.R, color.G, color.B, color.A);
        gl.Clear(ClearBufferMask.ColorBufferBit);
    }

    public void Clear() => ClearBackground(BackgroundColor);

    /// <summary>
    /// Translate the transformation matrix
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    public void Translate(float x, float y, float z)
    {
        TransformMatrix = Matrix4x4.CreateTranslation(x, y, z) * TransformMatrix;
    }

    /// <summary>
    /// Translate the transformation matrix
    /// </summary>
    /// <param name="vec"></param>
    public void Translate(Vector3 vec)
    {
        TransformMatrix = Matrix4x4.CreateTranslation(vec) * TransformMatrix;
    }

    /// <summary>
    /// Rotate the transformation matrix
    /// </summary>
    /// <param name="angle"></param>
    public void RotateX(float angle)
        => TransformMatrix = Matrix4x4.CreateRotationX(angle) * TransformMatrix;

    /// <summary>
    /// Rotate the transformation matrix
    /// </summary>
    /// <param name="angle"></param>
    public void RotateY(float angle)
        => TransformMatrix = Matrix4x4.CreateRotationY(angle) * TransformMatrix;

    /// <summary>
    /// Rotate the transformation matrix
    /// </summary>
    /// <param name="angle"></param>
    public void RotateZ(float angle)
        => TransformMatrix = Matrix4x4.CreateRotationZ(angle) * TransformMatrix;

    /// <summary>
    /// Rotate the transformation matrix in 2D
    /// </summary>
    /// <param name="angle"></param>
    public void Rotate(float angle)
        => TransformMatrix = Matrix4x4.CreateRotationZ(angle) * TransformMatrix;

    /// <summary>
    /// Scale the transformation matrix
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    public void Scale(float x, float y, float z)
        => TransformMatrix = Matrix4x4.CreateScale(x, y, z) * TransformMatrix;

    /// <summary>
    /// Scale the transformation matrix
    /// </summary>
    /// <param name="scale"></param>
    public void Scale(Vector3 scale)
        => TransformMatrix = Matrix4x4.CreateScale(scale) * TransformMatrix;
    
    public void ResetTransform()
        => TransformMatrix = BaseTransform;

    public void ClearTransformationStack()
        => transformStack.Clear();

    /// <summary>
    /// Push the current transformation matrix to the
    /// transform stack.
    /// </summary>
    public void PushTransform()
    {
        transformStack.Push(TransformMatrix);
    }

    /// <summary>
    /// Pop a matrix from the transformation stack, and set it to
    /// the current transformation matrix.
    /// </summary>
    public void PopTransform()
    {
        TransformMatrix = transformStack.Pop();
    }

    /// <summary>
    /// Create a shader.
    /// </summary>
    /// <param name="vsSource">The vertex shader source. If null, will use the default vertex shader.</param>
    /// <param name="fsSource">The fragment shader source. If null, will use the default fragment shader.</param>
    /// <returns></returns>
    public Shader CreateShader(string? vsSource = null, string? fsSource = null)
        => new(gl, vsSource, fsSource);
    
    /// <summary>
    /// Create a mesh with a custom buffer setup.
    /// </summary>
    /// <param name="config"></param>
    /// <returns></returns>
    public Mesh CreateMesh(MeshConfiguration config)
        => new(gl, config);
    
    /// <summary>
    /// Create a mesh with a standard buffer setup.
    /// </summary>
    /// <param name="indexed"></param>
    /// <returns></returns>
    public StandardMesh CreateMesh(bool indexed = false)
        => new(gl, indexed);
    
    /// <summary>
    /// Draw a mesh.
    /// </summary>
    /// <param name="mesh">The mesh to draw.</param>
    public void Draw(Mesh mesh)
    {
        DrawBatch();

        var shader = shaderValue ?? defaultShader;
        if (shader.HasUniform("uTransformMatrix"))
            shader.SetUniform("uTransformMatrix", TransformMatrix);
        
        mesh.Draw();
    }

    public unsafe void DrawBatch()
    {
        if (numVertices == 0) return;

        gl.BindVertexArray(batchVertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, batchBuffer);

        fixed (float* data = batchData)
        {
            gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, numVertices * VertexDataSize*sizeof(float), data);
        }

        var shader = shaderValue ?? defaultShader;
        shader.Use(gl);
        
        if (shader.HasUniform("uTransformMatrix"))
            shader.SetUniform("uTransformMatrix", Matrix4x4.Identity);
        
        gl.DrawArrays(GLEnum.Triangles, 0, numVertices);
        numVertices = 0;
    }

    private void CheckCapacity(uint newVertices)
    {
        if (numVertices + newVertices >= MaxVertices)
        {
            Console.WriteLine("Batch was full - flush batch");
            DrawBatch();
        }
    }

    private void PushVertex(float x, float y)
    {
        CheckCapacity(1);

        var vec = Vector4.Transform(new Vector4(x, y, 0f, 1f), TransformMatrix);

        uint i = numVertices * VertexDataSize;
        batchData[i++] = vec.X / vec.W;
        batchData[i++] = vec.Y / vec.W;
        batchData[i++] = vec.Z / vec.W;
        batchData[i++] = UV.X;
        batchData[i++] = UV.Y;
        batchData[i++] = DrawColor.R;
        batchData[i++] = DrawColor.G;
        batchData[i++] = DrawColor.B;
        batchData[i++] = DrawColor.A;

        numVertices++;
    }

    public void DrawTriangle(float x0, float y0, float x1, float y1, float x2, float y2)
    {
        CheckCapacity(3);
        PushVertex(x0, y0);
        PushVertex(x1, y1);
        PushVertex(x2, y2);
    }

    public void DrawTriangle(Vector2 a, Vector2 b, Vector2 c) => DrawTriangle(a.X, a.Y, b.X, b.Y, c.X, c.Y);

    public void DrawRectangle(float x, float y, float w, float h)
    {
        CheckCapacity(6);
        PushVertex(x, y);
        PushVertex(x, y+h);
        PushVertex(x+w, y);
        
        PushVertex(x+w, y);
        PushVertex(x, y+h);
        PushVertex(x+w, y+h);
    }

    public void DrawRectangle(Vector2 origin, Vector2 size) => DrawRectangle(origin.X, origin.Y, size.X, size.Y);
    public void DrawRectangle(Rectangle rectangle) => DrawRectangle(rectangle.Left, rectangle.Top, rectangle.Width, rectangle.Height);

    public void DrawLine(float x0, float y0, float x1, float y1)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        if (dx == 0f && dy == 0f) return;

        var dist = MathF.Sqrt(dx*dx + dy*dy);

        var perpX = dy / dist * LineWidth / 2f;
        var perpY = -dx / dist * LineWidth / 2f;

        CheckCapacity(6);
        PushVertex(x0 + perpX, y0 + perpY);
        PushVertex(x0 - perpX, y0 - perpY);
        PushVertex(x1 - perpX, y1 - perpY);
        PushVertex(x1 + perpX, y1 + perpY);
        PushVertex(x0 + perpX, y0 + perpY);
        PushVertex(x1 - perpX, y1 - perpY);
    }

    public void DrawLine(Vector2 a, Vector2 b) => DrawLine(a.X, a.Y, b.X, b.Y);

    public void DrawRectangleLines(float x, float y, float w, float h)
    {
        DrawRectangle(x, y, w-LineWidth, LineWidth); // top side
        DrawRectangle(x, y+LineWidth, LineWidth, h-LineWidth); // left side
        DrawRectangle(x, y+h-LineWidth, w-LineWidth, LineWidth); // bottom side
        DrawRectangle(x+w-LineWidth, y+LineWidth, LineWidth, h-LineWidth); // right side
    }

    private const float SmoothCircleErrorRate = 0.5f;

    public void DrawCircleSector(float x0, float y0, float radius, float startAngle, float endAngle, int segments)
    {
        // copied from raylib code
        if (radius <= 0f) radius = 0.1f; // Avoid div by zero

        // expects (endAngle > startAngle)
        // if not, swap
        if (endAngle < startAngle)
        {
            (endAngle, startAngle) = (startAngle, endAngle);
        }

        int minSegments = (int)MathF.Ceiling((endAngle - startAngle) / (MathF.PI / 2f));
        if (segments < minSegments)
        {
            // calc the max angle between segments based on the error rate (usually 0.5f)
            float th = MathF.Acos(2f * MathF.Pow(1f - SmoothCircleErrorRate/radius, 2f) - 1f);
            segments = (int)((endAngle - startAngle) * MathF.Ceiling(2f * MathF.PI / th) / (2f * MathF.PI));
            if (segments <= 0) segments = minSegments;
        }

        float stepLength = (float)(endAngle - startAngle) / segments;
        float angle = startAngle;

        for (int i = 0; i < segments; i++)
        {
            CheckCapacity(3);
            PushVertex(x0, y0);
            PushVertex(x0 + MathF.Cos(angle + stepLength) * radius, y0 + MathF.Sin(angle + stepLength) * radius);
            PushVertex(x0 + MathF.Cos(angle) * radius, y0  + MathF.Sin(angle) * radius);
            angle += stepLength;
        }
    }

    public void DrawRingSector(float x0, float y0, float radius, float startAngle, float endAngle, int segments)
    {
        // copied from raylib code
        if (radius <= 0f) radius = 0.1f; // Avoid div by zero

        // expects (endAngle > startAngle)
        // if not, swap
        if (endAngle < startAngle)
        {
            (endAngle, startAngle) = (startAngle, endAngle);
        }

        int minSegments = (int)MathF.Ceiling((endAngle - startAngle) / (MathF.PI / 2f));
        if (segments < minSegments)
        {
            // calc the max angle between segments based on the error rate (usually 0.5f)
            float th = MathF.Acos(2f * MathF.Pow(1f - SmoothCircleErrorRate/radius, 2f) - 1f);
            segments = (int)((endAngle - startAngle) * MathF.Ceiling(2f * MathF.PI / th) / (2f * MathF.PI));
            if (segments <= 0) segments = minSegments;
        }

        float stepLength = (float)(endAngle - startAngle) / segments;
        float angle = startAngle;

        // cap line
        /*DrawLine(
            x0, y0,
            x0 + MathF.Cos(angle) * radius, y0 + MathF.Sin(angle) * radius
        );*/

        for (int i = 0; i < segments; i++)
        {
            DrawLine(
                x0 + MathF.Cos(angle) * radius, y0 + MathF.Sin(angle) * radius,
                x0 + MathF.Cos(angle+stepLength) * radius, y0 + MathF.Sin(angle+stepLength) * radius
            );

            angle += stepLength;
        }

        // cap line
        /*DrawLine(
            x0, y0,
            x0 + MathF.Cos(angle) * radius, y0 + MathF.Sin(angle) * radius
        );*/
    }

    public void DrawCircleSector(Vector2 center, float radius, float startAngle, float endAngle, int segments)
        => DrawCircleSector(center.X, center.Y, radius, startAngle, endAngle, segments);
    
    public void DrawCircle(float x, float y, float radius, int segments = 36)
        => DrawCircleSector(x, y, radius, 0f, 2f * MathF.PI, segments);
    
    public void DrawCircle(Vector2 center, float radius, int segments = 36)
        => DrawCircleSector(center.X, center.Y, radius, 0f, 2f * MathF.PI, segments);

    public void DrawRingSector(Vector2 center, float radius, float startAngle, float endAngle, int segments)
        => DrawRingSector(center.X, center.Y, radius, startAngle, endAngle, segments);
    
    public void DrawRing(float x, float y, float radius, int segments = 36)
        => DrawRingSector(x, y, radius, 0f, 2f * MathF.PI, segments);
    
    public void DrawRing(Vector2 center, float radius, int segments = 36)
        => DrawRingSector(center.X, center.Y, radius, 0f, 2f * MathF.PI, segments);
}