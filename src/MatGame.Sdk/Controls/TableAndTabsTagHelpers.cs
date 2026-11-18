using System.Net;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatGame.Sdk.Controls;

/// <summary>
/// The one code base of a table (TABLE-01 .. TABLE-04): <c>&lt;md-table caption="Notes"&gt;&lt;thead&gt;...&lt;/thead&gt;&lt;tbody&gt;...&lt;/tbody&gt;&lt;/md-table&gt;</c>.
/// It draws the wrapper that scrolls sideways and the caption that screen readers read; every <c>td</c> carries <c>data-label</c> (on a phone a row becomes a card).
/// </summary>
[HtmlTargetElement("md-table")]
public sealed class TableTagHelper : TagHelper
{
    [HtmlAttributeName("caption")] public string Caption { get; set; } = string.Empty;

    [HtmlAttributeName("zebra")] public bool Zebra { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.Attributes.SetAttribute("class", "mat-table-wrap");
        output.PreContent.SetHtmlContent($"<table class=\"mat-table{(Zebra ? " mat-table--zebra" : string.Empty)}\"><caption class=\"mat-sr-only\">{WebUtility.HtmlEncode(Caption)}</caption>");
        output.PostContent.SetHtmlContent("</table>");
    }
}

/// <summary>
/// The one code base of a tab bar whose tabs are pages that belong together: <c>&lt;md-tabs label="Account"&gt;&lt;md-tab href="/Account" active&gt;Profile&lt;/md-tab&gt;&lt;/md-tabs&gt;</c>.
/// Several tab bars can stand on one page.
/// </summary>
[HtmlTargetElement("md-tabs")]
public sealed class TabsTagHelper : TagHelper
{
    [HtmlAttributeName("label")] public string Label { get; set; } = "Bereiche";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "nav";
        output.Attributes.SetAttribute("class", "mat-tabs");
        output.Attributes.SetAttribute("aria-label", Label);
    }
}

[HtmlTargetElement("md-tab", ParentTag = "md-tabs")]
public sealed class TabTagHelper : TagHelper
{
    [HtmlAttributeName("href")] public string Href { get; set; } = "#";

    [HtmlAttributeName("active")] public bool Active { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "a";
        output.Attributes.SetAttribute("class", Active ? "mat-tab is-active" : "mat-tab");
        output.Attributes.SetAttribute("href", Href);
        if (Active)
        {
            output.Attributes.SetAttribute("aria-current", "page");
        }
    }
}

/// <summary>
/// The one code base of tabs inside a page (the areas of one item: General, Security, Statistics): a group whose buttons switch panels without a reload (MatUi <c>data-tabs</c>).
/// <c>&lt;md-tab-group label="Areas"&gt;&lt;md-tab-button panel="p-general" icon="user" active&gt;General&lt;/md-tab-button&gt;...&lt;/md-tab-group&gt;</c> and
/// <c>&lt;md-tab-panel id="p-general" active&gt;...&lt;/md-tab-panel&gt;</c> inside the same <c>&lt;div data-tabs&gt;</c>. Several groups can stand on one page (the ids differ).
/// </summary>
[HtmlTargetElement("md-tab-group")]
public sealed class TabGroupTagHelper : TagHelper
{
    [HtmlAttributeName("label")] public string Label { get; set; } = "Bereiche";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.Attributes.SetAttribute("class", "mat-tabs");
        output.Attributes.SetAttribute("role", "tablist");
        output.Attributes.SetAttribute("aria-label", Label);
    }
}

[HtmlTargetElement("md-tab-button", ParentTag = "md-tab-group")]
public sealed class TabButtonTagHelper : TagHelper
{
    [HtmlAttributeName("panel")] public string Panel { get; set; } = string.Empty;

    [HtmlAttributeName("icon")] public string? Icon { get; set; }

    [HtmlAttributeName("active")] public bool Active { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var text = (await output.GetChildContentAsync()).GetContent();
        output.TagName = "button";
        output.Attributes.SetAttribute("type", "button");
        output.Attributes.SetAttribute("class", "mat-tab");
        output.Attributes.SetAttribute("role", "tab");
        output.Attributes.SetAttribute("id", "tab-" + Panel);
        output.Attributes.SetAttribute("aria-controls", Panel);
        output.Attributes.SetAttribute("aria-selected", Active ? "true" : "false");
        if (!Active)
        {
            output.Attributes.SetAttribute("tabindex", "-1");
        }

        var icon = Icon is null ? string.Empty : MatGame.Sdk.Web.Ui.Icon(Icon).ToString();
        output.Content.SetHtmlContent($"{icon}<span>{text}</span>");
    }
}

[HtmlTargetElement("md-tab-panel")]
public sealed class TabPanelTagHelper : TagHelper
{
    [HtmlAttributeName("id")] public string Id { get; set; } = string.Empty;

    [HtmlAttributeName("active")] public bool Active { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.Attributes.SetAttribute("class", "mat-tabpanel");
        output.Attributes.SetAttribute("role", "tabpanel");
        output.Attributes.SetAttribute("id", Id);
        output.Attributes.SetAttribute("aria-labelledby", "tab-" + Id);
        if (!Active)
        {
            output.Attributes.SetAttribute("hidden", "hidden");
        }
    }
}
