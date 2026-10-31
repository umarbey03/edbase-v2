using Zinnur.Application.Recordings.Services;
using Zinnur.Domain.Enums;
using Zinnur.Infrastructure.Services;

namespace Zinnur.IntegrationTests.Recordings;

/// <summary>
/// ffmpeg CHIQISH parametrlari (2026-10-31) — bazasiz, ffmpeg'siz.
///
/// ★ NIMA UCHUN KERAK: ovoz sozlamasi (64k mono) filtr grafida EMAS, AYNAN
/// shu argumentlarda yashaydi. Graf oltin testlari uni ko'rmaydi, haqiqiy
/// ffmpeg testi esa ffmpeg'siz mashinada o'tkazib yuboriladi — ya'ni bu
/// test bo'lmasa, `-b:a 128k` ga jimgina qaytish hech qayerda sezilmasdi.
/// </summary>
public class FfmpegArgumentsTests
{
    [Fact]
    public void Defaults_UseCrf24_And64kMonoAudio_WithoutBitrateCap()
    {
        var args = FfmpegRecordingComposer.BuildArguments(Plan(CompositionPlanSettings.Default), "/s", "/s/out.mp4");

        ValueOf(args, "-crf").Should().Be("24");
        ValueOf(args, "-preset").Should().Be("veryfast");
        ValueOf(args, "-b:a").Should().Be("64k");
        ValueOf(args, "-ac").Should().Be("1");
        ValueOf(args, "-ar").Should().Be("48000");

        // CRF — sifat boshqaruvi; bitreyt chegarasi ekrandagi matnni
        // xiralashtirardi (registrdagi `compose_crf` izohi).
        args.Should().NotContain("-maxrate");
        args.Should().NotContain("-b:v");
    }

    [Fact]
    public void Legacy_RestoresTheOldAudio()
    {
        var args = FfmpegRecordingComposer.BuildArguments(Plan(CompositionPlanSettings.Legacy1080p), "/s", "/s/out.mp4");

        ValueOf(args, "-crf").Should().Be("21");
        ValueOf(args, "-b:a").Should().Be("128k");
        ValueOf(args, "-ac").Should().Be("2");
    }

    private static CompositionPlan Plan(CompositionPlanSettings settings) =>
        new(
            RecordingId: 1,
            TargetObjectKey: "recordings/test/1.mp4",
            Inputs:
            [
                new CompositionInput(
                    Index: 0,
                    TrackId: 1,
                    Kind: RecordingTrackKind.CameraVideo,
                    ObjectKey: "raw/1/1/cam.webm",
                    FileName: "00-cam.webm",
                    ItsOffsetSeconds: 0,
                    ExpectedDurationMs: 10_000),
            ],
            FilterGraph: "[0:v]null[v];anullsrc[a]",
            TimelineSeconds: 10,
            Preset: settings.Preset,
            Crf: settings.Crf,
            Warning: null,
            AudioBitrateKbps: settings.AudioBitrateKbps,
            AudioChannels: settings.AudioChannels);

    private static string ValueOf(List<string> args, string flag)
    {
        var index = args.IndexOf(flag);

        index.Should().BeGreaterThanOrEqualTo(0, $"{flag} argumentlarda bo'lishi shart");

        return args[index + 1];
    }
}
