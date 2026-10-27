namespace ScreenPin.Core;

public readonly record struct GPoint(int X, int Y);

/// <summary>手势的最终判定结果：贴图 / 直接取字 / 无动作。</summary>
public enum GestureAction
{
    None,
    Pin,       // 圈选（闭合/弯曲）或 ↓→↑：生成置顶贴图
    CopyText,  // ↓←↑：对区域 OCR 并把文字复制到剪贴板
}

/// <summary>
/// 手势判定的全部可调参数（物理像素）。
/// 判定分层：距离阈值管“何时开始”，笔画/形状管“执行什么动作”，大小下限管“过滤误触”。
/// </summary>
public static class GestureConfig
{
    /// <summary>距按下点超过该距离才进入手势模式（在此之前视为普通右键）。可在设置里调。</summary>
    public static int ArmThreshold { get; set; } = 25;

    /// <summary>轨迹采样：距上一点至少这么远才记录</summary>
    public const int MinSampleDistance = 3;

    /// <summary>有效截图区域的最小边长</summary>
    public const int MinRegionSize = 30;

    /// <summary>有效手势的最少采样点数</summary>
    public const int MinPoints = 5;

    // ---- 圈判定（winding：方向角持续同向旋转）----
    // 圆与乱画的本质区别：圆平滑旋转累计约一整圈，乱画正反打转净旋转为零。

    /// <summary>圈要求的最小净旋转圈数（0.75 = 270°，容忍不封口的圈）</summary>
    public const double LoopMinTurns = 0.75;

    /// <summary>圈允许的急逆转次数（相邻采样方向变化 &gt; 90°；乱画会远超此数）</summary>
    public const int LoopMaxReversals = 4;

    /// <summary>轨迹点数上限，防御异常输入</summary>
    public const int MaxPoints = 4000;
}

public enum GesturePhase
{
    Idle,
    Armed,  // 右键已按下，还没超过阈值
    Active  // 手势模式，正在记录轨迹
}

/// <summary>松开右键时的完整结果。</summary>
public readonly record struct FinishResult(
    bool Swallow,       // 是否吞掉这次 RightUp
    bool ReplayClick,   // 是否需要回放一次普通右键（按下+松开都没怎么动）
    bool WasActive,     // 松开前是否处于手势模式
    GestureAction Action,
    CaptureRegion Region,
    IReadOnlyList<GPoint> Points);

/// <summary>
/// 手势状态机。在钩子线程上运行，只做轻量计算。
/// 吞放策略：右键按下即吞下；若最终没有形成手势，普通点击用 SendInput 回放。
/// 形状判定（只有 U 形和圆两种形状会触发动作，其余一律拒绝）：
///   U 形 = 起点终点都在上沿 + 顶带未被大幅横穿 + 底边净位移明确：
///     起笔在右、收笔在左（↓←↑ 意图）= 取字；起笔在左（↓→↑ 意图）= 框选；
///   圆 = 方向角持续同向旋转（winding ≥ 0.75 圈且急逆转 ≤ 4 次）= 框选；
///   直线、波浪、乱画、锯齿 = 拒绝（轨迹淡出，无任何动作）。
/// </summary>
public sealed class GestureTracker
{
    private readonly object _gate = new();
    private readonly List<GPoint> _points = new();
    private GesturePhase _phase = GesturePhase.Idle;
    private GPoint _start;
    private GPoint _last;

    public GesturePhase Phase
    {
        get { lock (_gate) return _phase; }
    }

    /// <summary>右键按下：进入 Armed，吞下事件（是点击还是手势，等松开时见分晓）。</summary>
    public bool OnRightDown(int x, int y)
    {
        lock (_gate)
        {
            _phase = GesturePhase.Armed;
            _start = new GPoint(x, y);
            _last = _start;
            _points.Clear();
            _points.Add(_start);
            return true;
        }
    }

    /// <summary>
    /// 移动：永不吞掉——低级钩子吞掉移动事件会把光标直接冻在原地。
    /// 应用没见过被吞掉的“右键按下”，多收一些悬停移动完全无害。
    /// 返回 (是否记录了新点, 是否刚进入手势模式)。
    /// </summary>
    public (bool PointAppended, bool BecameActive) OnMove(int x, int y)
    {
        lock (_gate)
        {
            if (_phase == GesturePhase.Idle)
            {
                return (false, false);
            }

            var p = new GPoint(x, y);
            if (Dist(p, _last) < GestureConfig.MinSampleDistance)
            {
                return (false, false);
            }

            if (_points.Count < GestureConfig.MaxPoints)
            {
                _points.Add(p);
            }

            if (_phase == GesturePhase.Armed && Dist(p, _start) >= GestureConfig.ArmThreshold)
            {
                _phase = GesturePhase.Active;
                _last = p;
                return (true, true);
            }

            _last = p;
            return (_phase == GesturePhase.Active, false);
        }
    }

    /// <summary>右键松开：结算。普通点击 → 回放；手势 → 判定形状并给出动作。</summary>
    public FinishResult OnRightUp(int x, int y)
    {
        lock (_gate)
        {
            var phase = _phase;
            var points = _points.ToArray();
            var region = CaptureRegion.FromPoints(points);
            _phase = GesturePhase.Idle;

            switch (phase)
            {
                case GesturePhase.Idle:
                    return new FinishResult(false, false, false, GestureAction.None, region, points);
                case GesturePhase.Armed: // 没超过阈值：是一次普通右键，回放给系统
                    return new FinishResult(true, true, false, GestureAction.None, region, points);
                default:
                    return new FinishResult(true, false, true, Classify(points, region), region, points);
            }
        }
    }

    /// <summary>外部取消（例如托盘暂停），当前手势直接作废。</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _phase = GesturePhase.Idle;
            _points.Clear();
        }
    }

    public GPoint[] PointsSnapshot()
    {
        lock (_gate) return _points.ToArray();
    }

    private static double Dist(GPoint a, GPoint b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>
    /// 形状 → 动作（方向意图优先，对画法精度宽容）。
    /// U 形意图 = 起点、终点都在上沿 + 底边净位移明确（方向即意图：
    /// 向左收 = 取字，向右收 = 框选）+ 顶带没有被大幅横穿（画圈才会横穿顶边，
    /// U 的上端轻微闭合蜷曲不会）。不满足则回落到圈选规则。
    /// </summary>
    private static GestureAction Classify(GPoint[] points, CaptureRegion region)
    {
        if (points.Length < GestureConfig.MinPoints)
        {
            return GestureAction.None;
        }
        if (region.Width < GestureConfig.MinRegionSize || region.Height < GestureConfig.MinRegionSize)
        {
            return GestureAction.None;
        }

        double top = region.Y;
        double width = region.Width;
        double height = region.Height;
        var start = points[0];
        var end = points[^1];

        // 顶带（上 25%）的水平行程总量：圈 ≈ 横穿整个宽度，U 只有两端的零星动作
        double topTravel = HorizontalTravel(points, top, top + height * 0.25);
        // 底带（下 55%）的净水平位移：U 底边从一侧到另一侧，方向即意图
        double bottomNet = NetDisplacementX(points, top + height * 0.45, top + height);

        bool startHigh = start.Y <= top + height * 0.4;
        bool endHigh = end.Y <= top + height * 0.4;

        if (topTravel < 0.5 * width
            && startHigh && endHigh
            && Math.Abs(bottomNet) >= 0.3 * width)
        {
            return bottomNet < 0 ? GestureAction.CopyText : GestureAction.Pin;
        }

        // 圆：方向角持续同向旋转（平滑绕一圈）。
        // 乱画/锯齿/折线因急逆转多、净旋转为零而出局；只有 U 形和圆能触发框图。
        return IsSmoothLoop(points) ? GestureAction.Pin : GestureAction.None;
    }

    /// <summary>
    /// 圈判定（winding number）：把轨迹相邻采样点的方向角变化累加成有符号旋转量。
    /// 圆/椭圆/圆角矩形套索：平滑旋转累计 ±2π，几乎无急逆转；
    /// 乱画：正反打转互相抵消，净旋转趋近 0，急逆转频发。
    /// 大于 90° 的方向突变计为急逆转，不参与旋转量累计。
    /// </summary>
    private static bool IsSmoothLoop(GPoint[] points)
    {
        const double reversalThreshold = Math.PI / 2;
        double winding = 0; // 有符号平滑旋转总量（弧度）
        int reversals = 0;
        double prevAngle = double.NaN;

        for (int i = 1; i < points.Length; i++)
        {
            double dx = points[i].X - points[i - 1].X;
            double dy = points[i].Y - points[i - 1].Y;
            if (dx == 0 && dy == 0)
            {
                continue;
            }
            double angle = Math.Atan2(dy, dx);
            if (!double.IsNaN(prevAngle))
            {
                double delta = angle - prevAngle;
                if (delta > Math.PI)
                {
                    delta -= 2 * Math.PI;
                }
                else if (delta <= -Math.PI)
                {
                    delta += 2 * Math.PI;
                }

                if (Math.Abs(delta) > reversalThreshold)
                {
                    reversals++;
                }
                else
                {
                    winding += delta; // 采样噪声零均值，只有真实旋转才会累计起来
                }
            }
            prevAngle = angle;
        }

        double turns = Math.Abs(winding) / (2 * Math.PI);
        return turns >= GestureConfig.LoopMinTurns && reversals <= GestureConfig.LoopMaxReversals;
    }

    /// <summary>带内（yLo..yHi）轨迹的水平行程总量（左右往复都累计）。</summary>
    private static double HorizontalTravel(GPoint[] points, double yLo, double yHi)
    {
        double travel = 0;
        for (int i = 1; i < points.Length; i++)
        {
            double midY = (points[i].Y + points[i - 1].Y) / 2.0;
            if (midY < yLo || midY > yHi)
            {
                continue;
            }
            travel += Math.Abs(points[i].X - points[i - 1].X);
        }
        return travel;
    }

    /// <summary>带内（yLo..yHi）首末采样点的水平净位移（往复抖动相互抵消）。</summary>
    private static double NetDisplacementX(GPoint[] points, double yLo, double yHi)
    {
        int first = -1;
        int last = -1;
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i].Y >= yLo && points[i].Y <= yHi)
            {
                if (first < 0)
                {
                    first = i;
                }
                last = i;
            }
        }
        return first < 0 ? 0 : points[last].X - points[first].X;
    }
}
