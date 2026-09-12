using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Advanced.Win2D;
using Microsoft.UI.Reactor.Core;
using Net.Codecrete.QrCodeGenerator;
using System.Numerics;
using static Microsoft.UI.Reactor.Advanced.Factories;

namespace Tonarink.Utilities;

static class QrCodeCanvas
{
    private const float CanvasSize = 240;
    private const float BorderWidth = 4;

    public static Element Render(string text, string automationName)
    {
        var code = QrCode.EncodeText(text, QrCode.Ecc.Medium);
        return Win2DCanvas(
                (drawingSession, _) => Draw(code, drawingSession),
                redrawKey: text)
            .ClearColor(Colors.White)
            .Size(CanvasSize, CanvasSize)
            .HAlign(Microsoft.UI.Xaml.HorizontalAlignment.Center)
            .AutomationName(automationName);
    }

    private static void Draw(QrCode code, CanvasDrawingSession drawingSession)
    {
        drawingSession.Antialiasing = CanvasAntialiasing.Aliased;

        var totalModules = code.Size + 2 * BorderWidth;
        var scale = CanvasSize / totalModules;
        drawingSession.Transform =
            Matrix3x2.CreateTranslation(BorderWidth, BorderWidth)
            * Matrix3x2.CreateScale(scale);

        using var pathBuilder = new CanvasPathBuilder(drawingSession);
        foreach (var polygon in code.ToOutlines())
        {
            var vertices = polygon.Vertices;
            pathBuilder.BeginFigure(vertices[0].X, vertices[0].Y);
            for (var index = 1; index < vertices.Count; index++)
                pathBuilder.AddLine(vertices[index].X, vertices[index].Y);
            pathBuilder.EndFigure(CanvasFigureLoop.Closed);
        }

        using var geometry = CanvasGeometry.CreatePath(pathBuilder);
        drawingSession.FillGeometry(geometry, Colors.Black);
    }
}
