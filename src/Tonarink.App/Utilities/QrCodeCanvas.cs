using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Advanced.Win2D;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Net.Codecrete.QrCodeGenerator;
using System.Numerics;
using static Microsoft.UI.Reactor.Advanced.Factories;

namespace Tonarink.Utilities;

static class QrCodeCanvas
{
    private const float CanvasSize = 240;
    private const float BorderWidth = 4;
    private const float CardCornerRadius = 12;

    public static Element Render(string text, string automationName)
    {
        var code = QrCode.EncodeText(text, QrCode.Ecc.Medium);
        return Win2DCanvas(
                (drawingSession, _) => Draw(code, drawingSession),
                redrawKey: text)
            .ClearColor(Colors.Transparent)
            .Size(CanvasSize, CanvasSize)
            .HAlign(Microsoft.UI.Xaml.HorizontalAlignment.Center)
            .AutomationName(automationName)
            .OnMountAdd(element =>
            {
                if (element is not FrameworkElement canvas)
                    return;

                var visual = ElementCompositionPreview.GetElementVisual(canvas);
                var geometry = visual.Compositor.CreateRoundedRectangleGeometry();
                geometry.Size = new Vector2(CanvasSize, CanvasSize);
                geometry.CornerRadius = new Vector2(CardCornerRadius, CardCornerRadius);
                visual.Clip = visual.Compositor.CreateGeometricClip(geometry);
            })
            .OnUnmountAdd(element =>
            {
                if (element is FrameworkElement canvas)
                    ElementCompositionPreview.GetElementVisual(canvas).Clip = null;
            });
    }

    private static void Draw(QrCode code, CanvasDrawingSession drawingSession)
    {
        drawingSession.Clear(Colors.Transparent);
        drawingSession.Antialiasing = CanvasAntialiasing.Antialiased;

        drawingSession.FillRoundedRectangle(
            0,
            0,
            CanvasSize,
            CanvasSize,
            CardCornerRadius,
            CardCornerRadius,
            Colors.White);
        drawingSession.DrawRoundedRectangle(
            0.5f,
            0.5f,
            CanvasSize - 1,
            CanvasSize - 1,
            CardCornerRadius,
            CardCornerRadius,
            Windows.UI.Color.FromArgb(34, 0, 0, 0),
            1);

        var totalModules = code.Size + 2 * BorderWidth;
        var moduleSize = CanvasSize / totalModules;
        var moduleInset = moduleSize * 0.055f;
        var moduleCornerRadius = moduleSize * 0.2f;

        for (var y = 0; y < code.Size; y++)
        {
            for (var x = 0; x < code.Size; x++)
            {
                if (!code.GetModule(x, y) || IsFinderModule(code.Size, x, y))
                    continue;

                var left = (BorderWidth + x) * moduleSize + moduleInset;
                var top = (BorderWidth + y) * moduleSize + moduleInset;
                var size = moduleSize - 2 * moduleInset;
                drawingSession.FillRoundedRectangle(
                    left,
                    top,
                    size,
                    size,
                    moduleCornerRadius,
                    moduleCornerRadius,
                    Colors.Black);
            }
        }

        DrawFinderPattern(drawingSession, 0, 0, moduleSize);
        DrawFinderPattern(drawingSession, code.Size - 7, 0, moduleSize);
        DrawFinderPattern(drawingSession, 0, code.Size - 7, moduleSize);
    }

    private static bool IsFinderModule(int codeSize, int x, int y) =>
        x < 7 && y < 7
        || x >= codeSize - 7 && y < 7
        || x < 7 && y >= codeSize - 7;

    private static void DrawFinderPattern(
        CanvasDrawingSession drawingSession,
        int originX,
        int originY,
        float moduleSize)
    {
        var left = (BorderWidth + originX) * moduleSize;
        var top = (BorderWidth + originY) * moduleSize;

        drawingSession.FillRoundedRectangle(
            left,
            top,
            7 * moduleSize,
            7 * moduleSize,
            1.12f * moduleSize,
            1.12f * moduleSize,
            Colors.Black);
        drawingSession.FillRoundedRectangle(
            left + moduleSize,
            top + moduleSize,
            5 * moduleSize,
            5 * moduleSize,
            0.8f * moduleSize,
            0.8f * moduleSize,
            Colors.White);
        drawingSession.FillRoundedRectangle(
            left + 2 * moduleSize,
            top + 2 * moduleSize,
            3 * moduleSize,
            3 * moduleSize,
            0.55f * moduleSize,
            0.55f * moduleSize,
            Colors.Black);
    }
}
