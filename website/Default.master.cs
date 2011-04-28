using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;

public partial class _Master : System.Web.UI.MasterPage
{
    protected void Page_Load(object sender, EventArgs e)
    {
        btn_dashboard.NavigateUrl =
            "~/Default.aspx";

        btn_settings.NavigateUrl =
            "~/Default.aspx" +
            "?action=settings&sourceUrl=" +
            Server.UrlEncode(Request.Url.ToString());

        btn_help.NavigateUrl =
            "~/Help.aspx?sourceUrl=" +
            Server.UrlEncode(Request.Url.ToString());

        string namePrefix = "My ";
        string urlPrefix = "?";

        string alias = Request.QueryString["alias"];
        if (alias != null)
        {
            namePrefix = (alias == "*" ? "Everyone" : alias) + "'s ";
            urlPrefix += "alias=" + alias + "&";
        }

        // Changes history
        btn_changesHistory.Text = namePrefix + "changes history";
        btn_changesHistory.NavigateUrl = urlPrefix + "action=history&role=author";

        // Reviews history
        btn_reviewsHistory.Text = namePrefix + "reviews history";
        btn_reviewsHistory.NavigateUrl = urlPrefix + "action=history&role=reviewer";

        // Active reviews
        btn_activeReviews.NavigateUrl = "~/Default.aspx" + "?alias=*";

        // All reviews history
        // btn_allReviewsHistory.NavigateUrl = urlPrefix + "?alias=*&action=history";

        btn_stats.NavigateUrl =
            "~/Default.aspx" +
            "?action=stats&sourceUrl=" +
            Server.UrlEncode(Request.Url.ToString());
    }

    public string Title
    {
        get { return page_title.Text; }
        set { page_title.Text = value; }
    }

    public T FindControl<T>(string id)
        where T : Control
    {
        return (T)base.FindControl(id);
    }

    public ContentPlaceHolder Main
    {
        get { return MainPlaceHolder; }
    }
}
