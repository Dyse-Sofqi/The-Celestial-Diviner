using System.Windows;
using System.Windows.Controls.Primitives;

namespace TheCelestialDiviner.Helpers;

/// <summary>
/// 设置菜单弹层的显式定位回调（Placement = Custom）。
/// 系统的“菜单右对齐”辅助选项（SystemParameters.MenuDropAlignment，部分中文软件会置位）
/// 会把 WPF Popup 的 Top 摆位镜像成右对齐、Right 摆位翻转到左侧；Custom 摆位按回调坐标
/// 原样落地不受镜像，因此菜单定位全部走此处的显式计算：
/// 一级弹层左上角对齐菜单键按钮左下角（向下展开 = 下拉、左对齐；下方放不下时自动改用备选摆位
/// 向上展开），二级弹层对齐分组项右侧（向右展开）。
/// </summary>
public static class MenuPlacements
{
    /// <summary>偏移单位：一级弹层与目标之间的纵向间隙（DIP）。
    /// 纵向路径不触发二级弹层的同级关闭计时器（鼠标离开按钮后立即离开 Menu 区域，
    /// IsMouseOverSibling 恒 false），2px 呼吸间隙保留（下拉 / 上翻两个方向同理）。</summary>
    private const double Gap = 2;

    /// <summary>一级弹层：首选左上角对齐目标左下角 —— 向下展开（下拉）、左对齐；
    /// 备选左下角对齐目标左上角 —— 向上展开。
    /// 两个候选一并返回：WPF 按「候选矩形与所在显示器工作区的相交面积」择优（同分取靠前候选），
    /// 因此窗口贴屏幕下缘 / 最大化等下方放不下时自动翻到上方，菜单不会落到屏幕外
    /// （弹层 Child 位于 MenuItem 模板内 → Popup.GetScreenBounds 取工作区 rcWork 而非整个显示器，
    /// 任务栏不计入可用高度）。</summary>
    public static readonly CustomPopupPlacementCallback BelowTarget =
        (popupSize, targetSize, offset) => new[]
        {
            new CustomPopupPlacement(new Point(0, targetSize.Height + Gap), PopupPrimaryAxis.None),
            new CustomPopupPlacement(new Point(0, -popupSize.Height - Gap), PopupPrimaryAxis.None),
        };

    /// <summary>二级弹层：左上角对齐目标右上角 —— 向右展开、无缝贴合（无间隙）。
    /// 间隙会致慢速鼠标永远选不中子菜单：鼠标离开分组项、进入间隙期间仍悬在本应用
    /// Menu 上，MenuItem.IsMouseOverSibling = true → 按 MenuShowDelay（本机为 0，立即触发）
    /// 反选并关闭弹层，慢速穿越永远追不上；快速划过时 Leave→Enter 同帧、弹层
    /// MouseEnter 停掉关闭计时器方可幸免 —— 即“只有极快划入才能选中”的根因。
    /// 无缝贴合后，鼠标离开分组项的瞬间已命中弹层 HWND，悬停模式全程保持。</summary>
    public static readonly CustomPopupPlacementCallback RightOfTarget =
        (popupSize, targetSize, offset) =>
            new[] { new CustomPopupPlacement(new Point(targetSize.Width, 0), PopupPrimaryAxis.None) };
}
