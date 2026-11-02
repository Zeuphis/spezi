using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Spezi.Core.Recording;

internal sealed class ColorConverter : IDisposable
{
    readonly ID3D11Device _device;
    readonly ID3D11DeviceContext _context;
    readonly ID3D11VideoDevice _videoDevice;
    readonly ID3D11VideoContext _videoContext;
    readonly ID3D11VideoContext1? _videoContext1;
    ID3D11VideoProcessorEnumerator? _enumerator;
    ID3D11VideoProcessor? _processor;
    ID3D11Texture2D? _sourceCopy;
    ID3D11VideoProcessorInputView? _inputView;
    ID3D11Texture2D? _staging;
    int _srcWidth;
    int _srcHeight;
    int _dstWidth;
    int _dstHeight;

    public ColorConverter(ID3D11Device device, ID3D11DeviceContext context)
    {
        _device = device;
        _context = context;
        _videoDevice = device.QueryInterface<ID3D11VideoDevice>();
        _videoContext = context.QueryInterface<ID3D11VideoContext>();
        _videoContext1 = context.QueryInterfaceOrNull<ID3D11VideoContext1>();
    }

    public void Ensure(int srcWidth, int srcHeight, int dstWidth, int dstHeight)
    {
        srcWidth = AppSettings.MakeEven(srcWidth);
        srcHeight = AppSettings.MakeEven(srcHeight);
        dstWidth = AppSettings.MakeEven(dstWidth);
        dstHeight = AppSettings.MakeEven(dstHeight);
        if (_processor != null && _srcWidth == srcWidth && _srcHeight == srcHeight && _dstWidth == dstWidth && _dstHeight == dstHeight)
            return;

        DisposeProcessor();
        _srcWidth = srcWidth;
        _srcHeight = srcHeight;
        _dstWidth = dstWidth;
        _dstHeight = dstHeight;

        var description = new VideoProcessorContentDescription
        {
            InputFrameFormat = VideoFrameFormat.Progressive,
            InputFrameRate = new Rational(60, 1),
            InputWidth = (uint)srcWidth,
            InputHeight = (uint)srcHeight,
            OutputFrameRate = new Rational(60, 1),
            OutputWidth = (uint)dstWidth,
            OutputHeight = (uint)dstHeight,
            Usage = VideoUsage.PlaybackNormal
        };
        _videoDevice.CreateVideoProcessorEnumerator(ref description, out var enumerator).CheckError();
        _enumerator = enumerator;
        _videoDevice.CreateVideoProcessor(enumerator, 0, out var processor).CheckError();
        _processor = processor;

        var outputRect = new Vortice.RawRect(0, 0, dstWidth, dstHeight);
        _videoContext.VideoProcessorSetStreamSourceRect(processor, 0, true, new Vortice.RawRect(0, 0, srcWidth, srcHeight));
        _videoContext.VideoProcessorSetStreamDestRect(processor, 0, true, outputRect);
        _videoContext.VideoProcessorSetOutputTargetRect(processor, true, outputRect);
        if (_videoContext1 != null)
        {
            _videoContext1.VideoProcessorSetStreamColorSpace1(processor, 0, (ColorSpaceType)0);
            _videoContext1.VideoProcessorSetOutputColorSpace1(processor, (ColorSpaceType)8);
        }

    }

    public ID3D11Texture2D Convert(ID3D11Texture2D source)
    {
        if (_processor == null || _enumerator == null)
            throw new InvalidOperationException("The color converter is not ready.");

        var sourceDesc = source.Description;
        _sourceCopy ??= CreateBgra(_srcWidth, _srcHeight);
        var copyWidth = Math.Min(_srcWidth, (int)sourceDesc.Width);
        var copyHeight = Math.Min(_srcHeight, (int)sourceDesc.Height);
        var box = new Box(0, 0, 0, copyWidth, copyHeight, 1);
        _context.CopySubresourceRegion(_sourceCopy, 0, 0, 0, 0, source, 0, box);
        if (_inputView == null)
        {
            var inputViewDesc = new VideoProcessorInputViewDescription
            {
                FourCC = 0,
                ViewDimension = VideoProcessorInputViewDimension.Texture2D,
                Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0, ArraySlice = 0 }
            };
            _videoDevice.CreateVideoProcessorInputView(_sourceCopy, _enumerator, inputViewDesc, out var inputView).CheckError();
            _inputView = inputView;
        }

        // Each frame keeps its own surface until the asynchronous encoder releases it.
        // Render directly into that surface instead of a temporary pool plus a GPU copy.
        var target = CreateNv12(_dstWidth, _dstHeight);
        try
        {
            var outputViewDesc = new VideoProcessorOutputViewDescription
            {
                ViewDimension = VideoProcessorOutputViewDimension.Texture2D,
                Texture2D = new Texture2DVideoProcessorOutputView { MipSlice = 0 }
            };
            using var outputView = _videoDevice.CreateVideoProcessorOutputView(target, _enumerator, outputViewDesc);

            var stream = new VideoProcessorStream
            {
                Enable = true,
                OutputIndex = 0,
                InputFrameOrField = 0,
                PastFrames = 0,
                FutureFrames = 0,
                InputSurface = _inputView
            };
            _videoContext.VideoProcessorBlt(_processor, outputView, 0, 1, [stream]).CheckError();
            _context.Flush();
            return target;
        }
        catch { target.Dispose(); throw; }
    }

    public byte[] ReadNv12(ID3D11Texture2D texture)
    {
        var desc = texture.Description;
        var staging = _staging ??= _device.CreateTexture2D(new Texture2DDescription
        {
            Width = desc.Width,
            Height = desc.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.NV12,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read,
            BindFlags = BindFlags.None
        });
        _context.CopyResource(staging, texture);
        var mapped = _context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var height = (int)desc.Height;
            var width = (int)desc.Width;
            var size = width * height * 3 / 2;
            var data = new byte[size];
            unsafe
            {
                var source = (byte*)mapped.DataPointer;
                var ySize = width * height;
                for (var row = 0; row < height; row++)
                    new Span<byte>(source + row * (int)mapped.RowPitch, width).CopyTo(data.AsSpan(row * width, width));
                var uvHeight = height / 2;
                var uvSource = source + (int)mapped.RowPitch * height;
                for (var row = 0; row < uvHeight; row++)
                    new Span<byte>(uvSource + row * (int)mapped.RowPitch, width).CopyTo(data.AsSpan(ySize + row * width, width));
            }
            return data;
        }
        finally
        {
            _context.Unmap(staging, 0);
        }
    }

    ID3D11Texture2D CreateNv12(int width, int height) => _device.CreateTexture2D(new Texture2DDescription
    {
        Width = (uint)width,
        Height = (uint)height,
        MipLevels = 1,
        ArraySize = 1,
        Format = Format.NV12,
        SampleDescription = new SampleDescription(1, 0),
        Usage = ResourceUsage.Default,
        BindFlags = BindFlags.RenderTarget,
        CPUAccessFlags = CpuAccessFlags.None
    });

    ID3D11Texture2D CreateBgra(int width, int height) => _device.CreateTexture2D(new Texture2DDescription
    {
        Width = (uint)width,
        Height = (uint)height,
        MipLevels = 1,
        ArraySize = 1,
        Format = Format.B8G8R8A8_UNorm,
        SampleDescription = new SampleDescription(1, 0),
        Usage = ResourceUsage.Default,
        BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget
    });

    void DisposeProcessor()
    {
        _inputView?.Dispose();
        _inputView = null;
        _staging?.Dispose();
        _staging = null;
        _processor?.Dispose();
        _enumerator?.Dispose();
        _processor = null;
        _enumerator = null;
        _sourceCopy?.Dispose();
        _sourceCopy = null;
    }

    public void Dispose()
    {
        DisposeProcessor();
        _videoContext1?.Dispose();
        _videoContext.Dispose();
        _videoDevice.Dispose();
    }
}
