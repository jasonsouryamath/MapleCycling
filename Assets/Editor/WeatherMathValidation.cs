using UnityEngine;

/// <summary>
/// Headless tests for the weather/wind maths and state transitions.
/// Run: Unity -batchmode -quit -executeMethod WeatherMathValidation.Run -logFile weather_tests.log
/// Prints "[weather-tests] PASS n/n" or each FAIL, and exits non-zero on failure.
/// </summary>
public static class WeatherMathValidation
{
    private static int _pass, _fail;

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) _pass++; else { _fail++; Debug.LogError($"[weather-tests] FAIL {name} {detail}"); }
    }

    public static void Run()
    {
        _pass = _fail = 0;
        var phys = new CyclingPhysics();
        const float W = 200f;

        // Calm reproduces the pre-weather model: 0.5*rho*CdA*v^2.
        float v0 = 10f;
        Check("calm drag", Mathf.Abs(WindMath.AeroDragN(1.225f, 0.32f, v0) - 0.5f * 1.225f * 0.32f * 100f) < 1e-3f);
        float calm = phys.TerminalSpeed(W, 0f);

        // 20 km/h head / tail wind at equal power.
        phys.headwindMps = 20f / 3.6f; float head = phys.TerminalSpeed(W, 0f);
        phys.headwindMps = -20f / 3.6f; float tail = phys.TerminalSpeed(W, 0f);
        phys.headwindMps = 0f;
        Check("headwind slows", head < calm - 1.5f, $"calm {calm * 3.6f:0.0} head {head * 3.6f:0.0} km/h");
        Check("tailwind speeds up", tail > calm + 1.5f, $"tail {tail * 3.6f:0.0} km/h");
        Check("tailwind sane", tail * 3.6f < 70f);

        // Step() converges to TerminalSpeed in a headwind (integrator agrees with the solver).
        phys.headwindMps = 20f / 3.6f;
        float v = 5f; for (int i = 0; i < 6000; i++) v = phys.Step(v, W, 0f, 0f, 0.02f);
        Check("step converges", Mathf.Abs(v - head) < 0.15f, $"{v:0.00} vs {head:0.00}");
        phys.headwindMps = 0f;

        // Rider heading +Z at 10 m/s.
        var fwd = new Vector2(0f, 1f);
        var a = WindMath.Resolve(fwd, 10f, new Vector2(0f, -5.56f));      // air moving toward -Z: headwind
        Check("resolve headwind", Mathf.Abs(a.HeadwindMps - 5.56f) < 1e-3f && Mathf.Abs(a.AngleDeg) < 0.01f);
        var c = WindMath.Resolve(fwd, 0f, new Vector2(-5.56f, 0f));       // air moving toward -X = from the right
        Check("resolve crosswind from right", Mathf.Abs(c.AngleDeg - 90f) < 0.01f && c.CrosswindMps > 5f,
              $"angle {c.AngleDeg:0.0}");
        var m = WindMath.Resolve(fwd, 10f, new Vector2(-5.56f, 0f));      // riding: apparent wind swings forward
        Check("apparent angle forward of beam", m.AngleDeg > 0f && m.AngleDeg < 90f, $"{m.AngleDeg:0.0}");

        // Draft: on the wheel best, fades with gap and lateral offset, never boosts drag.
        float d1 = WindMath.DraftMultiplier(0.5f, 0f), d2 = WindMath.DraftMultiplier(4f, 0f),
              d3 = WindMath.DraftMultiplier(0.5f, 1.2f), dFar = WindMath.DraftMultiplier(20f, 0f);
        Check("draft monotonic", d1 < d2 && d2 < 1f && d3 > d1 && Mathf.Approximately(dFar, 1f));
        Check("draft bounded", d1 >= 0.59f);

        // Gusts: bounded, deterministic, non-negative.
        float gmax = 0f; bool det = true;
        for (int i = 0; i < 20000; i++)
        {
            float g = WindMath.Gust(i * 0.05, 42, 4f, 0.08f);
            gmax = Mathf.Max(gmax, g);
            det &= g >= 0f && g <= 4f && Mathf.Approximately(g, WindMath.Gust(i * 0.05, 42, 4f, 0.08f));
        }
        Check("gust bounded+deterministic", det && gmax > 1f, $"max {gmax:0.00}");

        // State transitions: endpoints exact, monotonic midway, air density sane.
        var s0 = new WeatherDirector.WeatherState { WindToXZ = Vector2.zero, Coverage = 0.1f, AirDensity = 1.2f };
        var s1 = new WeatherDirector.WeatherState { WindToXZ = new Vector2(8f, 0f), Coverage = 0.9f, AirDensity = 1.25f };
        Check("lerp endpoints", WeatherDirector.Lerp(s0, s1, 0f).Coverage == 0.1f && WeatherDirector.Lerp(s0, s1, 1f).Coverage == 0.9f);
        float mid = WeatherDirector.Lerp(s0, s1, 0.5f).WindToXZ.x;
        Check("lerp mid", mid > 3.9f && mid < 4.1f);
        Check("air density 15C", Mathf.Abs(WindMath.AirDensity(15f) - 1.225f) < 0.005f);

        Debug.Log($"[weather-tests] calm {calm * 3.6f:0.0} km/h, 20 km/h head {head * 3.6f:0.0}, tail {tail * 3.6f:0.0} @ {W} W");
        Debug.Log($"[weather-tests] {(_fail == 0 ? "PASS" : "FAIL")} {_pass}/{_pass + _fail}");
        if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(_fail == 0 ? 0 : 1);
    }
}
