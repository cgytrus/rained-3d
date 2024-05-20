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
        gl.Disable(EnableCap.CullFace);

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

    public Shader CreateShader(string? vsSource = null, string? fsSource = null)
    {
        return new Shader(gl, vsSource, fsSource);
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
        shader.SetUniform("uTransformMatrix", TransformMatrix);
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

        uint i = numVertices * VertexDataSize;
        batchData[i++] = x;
        batchData[i++] = y;
        batchData[i++] = 0f;
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
        DrawRectangle(x, y, w, LineWidth);
        DrawRectangle(x, y, LineWidth, h);
        DrawRectangle(x, y+h-LineWidth, w, LineWidth);
        DrawRectangle(x+w-LineWidth, y, LineWidth, h);
    }
}