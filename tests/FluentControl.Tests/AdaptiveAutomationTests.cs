using FluentControl.Services;
internal static class AdaptiveAutomationTests
{
    private static void Check(bool c, string m) { if (!c) throw new Exception(m); }
    private static void Reject(Action a) { try { a(); } catch (InvalidDataException) { return; } throw new Exception("Invalid light data accepted"); }
    internal static void Run()
    {
        var now = DateTimeOffset.Parse("2026-10-14T04:00:00Z");
        LightSample Sample(double lux, int seconds) => new() { Format = "fluentcontrol.light-sample", Version = 1, SourceId = "test", Timestamp = now.AddSeconds(seconds), Lux = lux, Valid = true };
        var good = Sample(100, 0);
        Check(LightFeed.Parse(LightFeed.Serialize(good), now, 1000).Lux == 100, "Common data schema");
        Reject(() => LightFeed.Parse(LightFeed.Serialize(Sample(0, -6)), now, 1000));
        Reject(() => LightFeed.Parse(LightFeed.Serialize(Sample(-1, 0)), now, 1000));
        Reject(() => LightFeed.Parse(LightFeed.Serialize(Sample(0, 6)), now, 1000));
        good.Valid = false; Reject(() => LightFeed.Parse(LightFeed.Serialize(good), now, 1000));
        good.Valid = true; good.Lux = null; Reject(() => LightFeed.Parse(LightFeed.Serialize(good), now, 1000));
        var smoothing = new LightSmoothing();
        Check(smoothing.Add(Sample(100,0)) is null && smoothing.Add(Sample(100,1)) is null, "Warmup needs three unique frames");
        Check(smoothing.Add(Sample(10000,2)) == 100, "A single spike must be rejected");
        Check(smoothing.Add(Sample(9999,2)) == 100, "Duplicate timestamp must not advance smoothing");
        var other = Sample(100,3); other.SourceId = "other"; Reject(() => smoothing.Add(other));
        smoothing.Reset(); Check(smoothing.Add(Sample(100,4)) is null, "Reconnection needs warmup");
        Check(SolarSchedule.NextBrightness(50, 51) is null && SolarSchedule.NextBrightness(50, 80) == 55 && SolarSchedule.NextBrightness(50, 10) == 45, "Brightness deadband and slew rate");
        Check(SolarSchedule.InDayRange(23,22,6) && SolarSchedule.InDayRange(3,22,6) && !SolarSchedule.InDayRange(12,22,6), "Wrapping theme schedule");
        var wuhan = SolarSchedule.Calculate(new DateTime(2026,10,14),30.5928,114.3055);
        Check(wuhan.Sunrise is not null && wuhan.Sunset is not null && wuhan.IsDay(now), "Wuhan midday should be daytime");
        var rise = wuhan.Sunrise!.Value.ToOffset(TimeSpan.FromHours(8)).TimeOfDay.TotalHours;
        var set = wuhan.Sunset!.Value.ToOffset(TimeSpan.FromHours(8)).TimeOfDay.TotalHours;
        Check(rise is > 6 and < 7 && set is > 17 and < 18.5, "Approximate autumn sunrise/sunset bounds");
        Check(SolarSchedule.Calculate(new DateTime(2026,6,21),80,0).PolarDay, "Polar day");
        Check(!SolarSchedule.Calculate(new DateTime(2026,12,21),80,0).PolarDay && SolarSchedule.Calculate(new DateTime(2026,12,21),80,0).Sunrise is null, "Polar night");
        int manual = 0; void OnManual() => manual++;
        ControlOperations.ManualBrightnessRequested += OnManual;
        try
        {
            var channel = new ControlChannel { Name="Test",Detail="",Glyph="",PropertyKey="brightness",Write=_=>{} };
            ControlOperations.Apply(new[] { channel }, 30, automatic:true); Check(manual==0,"Automatic writes cannot train/hold themselves");
            ControlOperations.Apply(new[] { channel }, 40); Check(manual==1,"Manual writes must notify hold");
        }
        finally { ControlOperations.ManualBrightnessRequested -= OnManual; }
        Console.WriteLine("PASS: light feed schema, freshness, smoothing, source identity, solar bounds, polar conditions and manual/automatic separation.");
    }
}
