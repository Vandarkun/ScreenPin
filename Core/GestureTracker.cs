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

    // ---- 圈选兜底判定（严格：宁可拒绝，不要误判成框图）----
    // 旧版兜底"方向变化>=3 即框图"配合 10px 最小笔画，手抖和波浪线都会中招。

    /// <summary>兜底笔画最小长度：比手抖幅度大一个量级，只有真正的转向才算一笔</summary>
    public const int LoopStrokeLength = 35;

    /// <summary>兜底要求的方向变化次数（以 LoopStrokeLength 量化）</summary>
    public const int LoopDirectionChanges = 3;

    /// <summary>兜底闭合判定：终点距起点 &lt;= max(基数, 对角线×比例)</summary>
    public const int LoopClosureBase = 30;
    public const double LoopClosureDiagonalRatio = 0.2;

    /// <summary>
    /// 兜底"绕圈"判定：轨迹总长 >= 对角线×该比例。
    /// 数学依据：圈/椭圆的周长 ≈ 对角线 × 2.2（π 的关系），
    /// 直线 = 1.0，普通波浪/斜线抖动 ≈ 1.2~1.5。
    /// </summary>
    public const double LoopPathRatio = 1.8;

    /// <summary>轨迹点数上限，防御异常输入</summary>
    public const int MaxPoints = 4000;
}

public enum GesturePhase
{
    Idle,
    Armed,  // 右键已按下，还没超过阈值
    Active  // 手势模式，正在记录轨迹
}

/// <summary>一笔：8 方向量化的方向扇区 + 行程长度。</summary>
public readonly record struct Stroke(int Sector, double Length);

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
/// 形状判定（按整体方向意图，不要求画得规矩）：
///   U 形 = 起点终点都在上沿 + 顶边中段未被轨迹碰到 + 水平跨度明确：
///     起笔在右、收笔在左（↓←↑ 意图）= 取字；起笔在左（↓→↑ 意图）= 框选；
///   其余按圈选规则（闭合或足够弯曲）判为贴图。
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

        // 常规圈选兜底（严格）：真闭合，或（真转向 + 路径长度接近绕圈周长）
        double diagonal = Math.Sqrt(width * width + height * height);
        double closureLimit = Math.Max(GestureConfig.LoopClosureBase, diagonal * GestureConfig.LoopClosureDiagonalRatio);
        if (Dist(start, end) <= closureLimit)
        {
            return GestureAction.Pin;
        }
        int changes = BuildStrokes(points, GestureConfig.LoopStrokeLength).Count - 1;
        bool loopish = PathLength(points) >= GestureConfig.LoopPathRatio * diagonal;
        return changes >= GestureConfig.LoopDirectionChanges && loopish
            ? GestureAction.Pin
            : GestureAction.None;
    }

    /// <summary>轨迹总长度。</summary>
    private static double PathLength(GPoint[] points)
    {
        double total = 0;
        for (int i = 1; i < points.Length; i++)
        {
            double dx = points[i].X - points[i - 1].X;
            double dy = points[i].Y - points[i - 1].Y;
            total += Math.Sqrt(dx * dx + dy * dy);
        }
        return total;
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

    /// <summary>
    /// 把轨迹量化为 8 方向并压缩成笔画序列：连续同方向合并，不足 minStroke 的折返当噪声并入。
    /// minStroke 决定"多大算一笔"：判圈用 LoopStrokeLength（35px，手抖不算），
    /// 波浪线在此尺度下通常只剩一两笔。
    /// </summary>
    private static List<Stroke> BuildStrokes(GPoint[] points, int minStroke)
    {
        var strokes = new List<Stroke>();
        int prevSector = -1;
        double run = 0;

        for (int i = 1; i < points.Length; i++)
        {
            double dx = points[i].X - points[i - 1].X;
            double dy = points[i].Y - points[i - 1].Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1)
            {
                continue;
            }

            int sector = SectorOf(dx, dy);
            if (prevSector < 0)
            {
                prevSector = sector;
                run = len;
            }
            else if (sector == prevSector)
            {
                run += len;
            }
            else if (run >= minStroke)
            {
                strokes.Add(new Stroke(prevSector, run));
                prevSector = sector;
                run = len;
            }
            else
            {
                run += len; // 太短的折返当噪声
            }
        }
        if (prevSector >= 0)
        {
            strokes.Add(new Stroke(prevSector, run));
        }
        return strokes;
    }

    private static int SectorOf(double dx, double dy)
    {
        double angle = Math.Atan2(dy, dx); // (-π, π]
        int sector = (int)Math.Round(angle / (Math.PI / 4)); // -4..4
        return ((sector % 8) + 8) % 8;
    }
}
