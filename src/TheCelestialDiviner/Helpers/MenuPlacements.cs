using System.Windows;
using System.Windows.Controls.Primitives;

namespace TheCelestialDiviner.Helpers;

/// <summary>
/// 设置菜单弹层的显式定位回调（Placement = Custom）。
/// 系统的“菜单右对齐”辅助选项（SystemParameters.MenuDropAlignment，部分中文软件会置位）
/// 会把 WPF Popup 的 Top 摆位镜像成右对齐、Right 摆位翻转到左侧；Custom 摆位按回调坐标
/// 原样落地不受镜像，因此菜单定位全部走此处的显式计算：
/// 一级弹层左下角对齐菜单键按钮左上角（向上展开、左对齐），二级弹层对齐分组项右侧（向右展开）。
/// </summary>
public static class MenuPlacements
{
    /// <summary>偏移单位：一级弹层与目标之间的间隙（DIP）。仅一级向上弹出使用——
    /// 纵向路径不触发二级弹层的同级关闭计时器（鼠标离开按钮后立即离开 Menu 区域，
    /// IsMouseOverSibling 恒 false），2px 呼吸间隙保留。</summary>
    private const double Gap = 2;

    /// <summary>一级弹层：左下角对齐目标左上角 —— 向上展开、左对齐。</summary>
    public static readonly CustomPopupPlacementCallback AboveTarget =
        (popupSize, targetSize, offset) =>
            new[] { new CustomPopupPlacement(new Point(0, -popupSize.Height - Gap), PopupPrimaryAxis.None) };

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
