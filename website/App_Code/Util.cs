//-----------------------------------------------------------------------
// <copyright>
// Copyright (C) Sergey Solyanik for The Malevich Project.
//
// This file is subject to the terms and conditions of the Microsoft Public License (MS-PL).
// See http://www.microsoft.com/opensource/licenses.mspx#Ms-PL for more details.
// </copyright>
//----------------------------------------------------------------------- 
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using System.Diagnostics;

using Malevich.Util;
using Malevich.Extensions;

namespace Malevich
{
    /// <summary>
    /// Various utility functions and data shared between the web site and web service.
    /// </summary>
    public static class Config
    {
        /// <summary>
        /// Test or production configuration?
        /// </summary>
        private const bool IsTest = false;

        /// <summary>
        /// Database connection string.
        /// </summary>
        public const string ConnectionString = IsTest ? "TestConnectionString" : "DataConnectionString";
    }
}

namespace Malevich.Extensions
{
    /// <summary>
    /// Object extensions: adds a few convenient methods to various types.
    /// </summary>
    public static class WebControlExtensions
    {
        /// <summary>
        /// Appends to a WebControl's list of CSS classes.
        /// </summary>
        /// <param name="text"> The WebControl to append the CSS class to. </param>
        /// <param name="cssClass"> The class to append. </param>
        public static T AppendCSSClass<T>(this T ctrl, string cssClass)
            where T : WebControl
        {
            Debug.Assert(ctrl != null);

            if (ctrl == null)
                return ctrl;

            if (ctrl.CssClass.IsNullOrEmpty())
                ctrl.CssClass = cssClass;
            else
            {
                // Only add the class if it isn't already there.
                var matches = System.Text.RegularExpressions.Regex.Matches(
                    ctrl.CssClass, "\\b" + cssClass + "\\b");
                if (matches.Count == 0)
                {
                    ctrl.CssClass = ctrl.CssClass + " " + cssClass;
                }
            }

            return ctrl;
        }

        public static T AddCSSClass<T>(this T ctrl, string cssClass)
            where T : WebControl
        {
            return AppendCSSClass(ctrl, cssClass);
        }

        public static T AddStyle<T>(this T ctrl, string name, string value)
            where T : WebControl
        {
            ctrl.Style[name] = value;
            return ctrl;
        }

        /// <summary>
        /// Creates a new T, adds it to this, and returns the created object.
        /// </summary>
        /// <typeparam name="T">The type to create. Must have a default constructor.</typeparam>
        /// <param name="_this">The WebControl that will contain the new object.</param>
        /// <param name="cssClass">The CSS class to assign to the newly created object.</param>
        /// <returns>Newly created T instance.</returns>
        public static T New<T>(this Control _this, string cssClass)
            where T : WebControl, new()
        {
            T t = new T() { CssClass = cssClass };
            _this.Add(t);
            return t;
        }

        /// <summary>
        /// Creates a new T, adds it to this, and returns the created object.
        /// </summary>
        /// <typeparam name="T">The type to create. Must have a default constructor.</typeparam>
        /// <param name="_this">The WebControl that will contain the new object.</param>
        /// <returns>Newly created T instance.</returns>
        public static T New<T>(this Control _this)
            where T : Control, new()
        {
            T t = new T();
            _this.Add(t);
            return t;
        }

        /// <summary>
        /// Adds ctrl to _this' set of controls.
        /// </summary>
        /// <typeparam name="T">Type of recipient control.</typeparam>
        /// <typeparam name="U">Type of control to add.</typeparam>
        /// <param name="_this">Instance of recipient control.</param>
        /// <param name="ctrl">Instance of control to add.</param>
        /// <returns>this (to allow easy statement composition)</returns>
        public static T Add<T, U>(this T _this, U ctrl)
            where T : Control
            where U : Control
        {
            _this.Controls.Add(ctrl);
            return _this;
        }

        public static T Add<T, U>(this T _this, IEnumerable<U> ctrls)
            where T : Control
            where U : Control
        {
            foreach (var elem in ctrls)
                _this.Add(elem);
            return _this;
        }

        /// <summary>
        /// Adds ctrl to _this' set of controls.
        /// </summary>
        /// <typeparam name="T">Type of recipient control.</typeparam>
        /// <typeparam name="U">Type of control to add.</typeparam>
        /// <param name="_this">Instance of recipient control.</param>
        /// <param name="ctrl">Instance of control to add.</param>
        /// <returns>this (to allow easy statement composition)</returns>
        public static T AddTo<T, U>(this T _this, U ctrl)
            where T : Control
            where U : Control
        {
            ctrl.Controls.Add(_this);
            return _this;
        }

        /// <summary>
        /// Adds ctrl to _this' set of controls.
        /// </summary>
        /// <typeparam name="T">Type of recipient control.</typeparam>
        /// <typeparam name="U">Type of control to add.</typeparam>
        /// <param name="_this">Instance of recipient control.</param>
        /// <param name="ctrl">Instance of control to add.</param>
        /// <returns>this (to allow easy statement composition)</returns>
        public static T AddText<T>(this T _this, string text)
            where T : Control
        {
            _this.Controls.Add(new Literal() { Text = text });
            return _this;
        }

        /// <summary>
        /// Adds a string to _this' set of controls, encapsulated as a DIV.
        /// </summary>
        /// <typeparam name="T">Type of control to add the string to.</typeparam>
        /// <param name="_this">Instance of control to add the string to.</param>
        /// <param name="text">The string to add.</param>
        /// <param name="cssClass">The CSS class for the string (will be applied to the DIV).</param>
        /// <returns>_this (to allow easy statement composition)</returns>
        public static T AddText<T>(this T _this, string text, string cssClass)
            where T : Control
        {
            return _this.Add(text.AsDiv(cssClass));
        }

        public static T AddBreak<T>(this T _this)
            where T : Control
        {
            return _this.Add("<br/>".AsLiteral());
        }

        /// <summary>
        /// Encloses string within a Literal control.
        /// </summary>
        public static Control AsLiteral(this string _this)
        {
            return new Literal() { Text = _this };
        }

        /// <summary>
        /// Encapsulates _this within an HTML tag.
        /// </summary>
        /// <typeparam name="T">Type to be encapsulated.</typeparam>
        /// <param name="_this">Instance to be encapsulated.</param>
        /// <param name="cssClass">The tag's CSS class.</param>
        /// <returns>The created WebControl.</returns>
        public static WebControl As<T>(this T _this, HtmlTextWriterTag tag)
            where T : Control
        {
            return new WebControl(tag).Add(_this);
        }

        /// <summary>
        /// Encapsulates _this within an HTML tag.
        /// </summary>
        /// <param name="_this">String to be encapsulated.</param>
        /// <param name="cssClass">The tag's CSS class.</param>
        /// <returns>The created WebControl.</returns>
        public static WebControl As(this string _this, HtmlTextWriterTag tag)
        {
            return new WebControl(tag).Add(_this.AsLiteral());
        }

        /// <summary>
        /// Encapsulates _this within a DIV.
        /// </summary>
        /// <typeparam name="T">Type to be encapsulated.</typeparam>
        /// <param name="_this">Instance to be encapsulated.</param>
        /// <param name="cssClass">The DIV's CSS class.</param>
        /// <returns>_this (to allow easy statement composition)</returns>
        public static Control AsDiv<T>(this T _this, string cssClass)
            where T : Control
        {
            return new Panel() { CssClass = cssClass }.Add(_this);
        }

        /// <summary>
        /// Encapsulates _this within a DIV.
        /// </summary>
        /// <typeparam name="T">Type to be encapsulated.</typeparam>
        /// <param name="_this">Instance to be encapsulated.</param>
        /// <returns>_this (to allow easy statement composition)</returns>
        public static Control AsDiv<T>(this T _this)
            where T : Control
        {
            return new Panel().Add(_this);
        }

        /// <summary>
        /// Encapsulates a string within a DIV.
        /// </summary>
        /// <param name="_this">String to be encapsulated.</param>
        /// <param name="cssClass">The DIV's CSS class.</param>
        /// <returns>_this (to allow easy statement composition)</returns>
        public static Control AsDiv(this string _this, string cssClass)
        {
            return _this.AsLiteral().As(HtmlTextWriterTag.Div).AppendCSSClass(cssClass);
        }

        /// <summary>
        /// Encapsulates a string within a DIV.
        /// </summary>
        /// <param name="_this">String to be encapsulated.</param>
        /// <returns>_this (to allow easy statement composition)</returns>
        public static Control AsDiv(this string _this)
        {
            return _this.AsLiteral().As(HtmlTextWriterTag.Div);
        }

        public static T FindControl<T>(this Control ctrl, string id)
            where T : Control
        {
            return (T)ctrl.FindControl(id);
        }
    }

    public static class ControlExtensions
    {
        /// <summary>
        /// Creates and adds a new label to the active page.
        /// </summary>
        /// <param name="contents">The label contents.</param>
        /// <returns>The new label.</returns>
        public static Label AddLabel(this Control content, string contents)
        {
            var label = new Label() { Text = contents };
            content.Controls.Add(label);
            return label;
        }
    }
}

namespace Malevich.Util.TableGen
{
    public class Cell : WebControl
    {
        public Cell()
            : base(HtmlTextWriterTag.Td)
        {
        }

        private Literal _Text;
        public string Text
        {
            get
            {
                if (_Text == null)
                    _Text = this.New<Literal>();
                return _Text.Text;
            }

            set
            {
                if (_Text == null)
                    _Text = this.New<Literal>();
                _Text.Text = value;
            }
        }

        public int ColumnSpan
        {
            get
            {
                int colspan = 1;
                int.TryParse(this.Attributes["colspan"] ?? "1", out colspan);
                return colspan;
            }
            set
            {
                this.Attributes["colspan"] = value.ToString();
            }
        }
    }

    public class Row : WebControl
    {
        public Row(int columnCount, string[] columnClasses, HtmlTextWriterTag tag)
            : base(tag)
        {
            for (int i = 0; i < columnCount; ++i)
            {
                var cell = new Cell().AppendCSSClass("Col" + i);
                base.Controls.Add(cell);
                if (columnClasses != null)
                    cell.AppendCSSClass(columnClasses[i]);
            }

            ApplyColumnStyles = true;
        }

        public Row(int columnCount, string[] columnClasses)
            : this(columnCount, columnClasses, HtmlTextWriterTag.Tr)
        { }

        public Row(int columnCount)
            : this(columnCount, null)
        { }

        public Cell this[int key]
        {
            get { return (Cell)base.Controls[key]; }
            set { base.Controls.AddAt(key, value); }
        }

        public int Count
        { get { return base.Controls.Count; } }

        public override ControlCollection Controls
        {
            get
            {
                return base.Controls;
            }
        }

        internal ColumnGroup _ColumnGroup;

        public Cell this[string key]
        {
            get
            {
                return this[_ColumnGroup[key].Index];
            }
        }

        public bool ApplyColumnStyles
        {
            get;
            set;
        }
    }

    public delegate void ItemAddedEventHandler(Object item, RowGroup owner);

    public class RowGroup : WebControl
    {
        public readonly int ColumnCount;
        protected bool RenderChildrenOnly;

        protected RowGroup(int columnCount, HtmlTextWriterTag tag)
            : base(tag)
        {
            ColumnCount = columnCount;
            RenderChildrenOnly = false;
            ItemAdded += OnItemAdded; // Self-notification.
        }

        public RowGroup(int columnCount)
            : this(columnCount, HtmlTextWriterTag.Tbody)
        {
            this.AppendCSSClass("RowGroup");
        }

        public Row CreateRow()
        {
            return new Row(ColumnCount) { _ColumnGroup = _ColumnGroup };
        }

        public RowGroup CreateRowGroup()
        {
            return new RowGroup(ColumnCount) { _ColumnGroup = _ColumnGroup };
        }

        public virtual void AddItem(Control item)
        {
            this.Add(item);
            if (ItemAdded != null)
                ItemAdded(item, this);
        }

        public virtual void AddRow(Row row)
        {
            this.AddItem(row);
        }

        public virtual void AddRowGroup(RowGroup grp)
        {
            this.AddItem(grp);
        }

        public Row this[int key]
        { get { return (Row)this.Controls[key]; } }

        private bool _IsHeader;
        public bool IsHeader
        { get { return _IsHeader; } set { _IsHeader = value; } }

        internal ColumnGroup _ColumnGroup { get; set; }

        public event ItemAddedEventHandler ItemAdded;

        protected virtual void OnItemAdded(object item, RowGroup owner)
        {
            if (item is RowGroup)
            {
                var grp = item as RowGroup;
                grp.ItemAdded += OnItemAdded;
                grp._ColumnGroup = _ColumnGroup;
            }
            else if (item is Row)
            {
                (item as Row)._ColumnGroup = _ColumnGroup;
                if (IsHeader)
                    (item as Row).ApplyColumnStyles = false;
            }
        }

        protected virtual void _VerifyAdd(Object item)
        {
            if (item is Row)
            {
                var row = item as Row;
                if (row.Count > ColumnCount)
                    throw new ArgumentException("Row's cell count is greater than the parent's column count");
            }
            else if (item is RowGroup)
            {
                var grp = item as RowGroup;
                if (grp.ColumnCount > ColumnCount)
                    throw new ArgumentException("RowGroup's cell count is greater than the parent's column count");
            }
        }

        protected delegate void ItemCallback(Object item);

        protected void _DepthFirstIterate(Object item, ItemCallback callback)
        {
            callback(item);
            if (item is RowGroup)
            {
                var grp = item as RowGroup;
                if (!grp.IsHeader)
                {
                    foreach (var ctrl in grp.Controls)
                        _DepthFirstIterate(ctrl, callback);
                }
            }
        }

        protected override void Render(HtmlTextWriter writer)
        {
            if (RenderChildrenOnly)
                RenderChildren(writer);
            else
                base.Render(writer);
        }
    }

    public class Column : WebControl
    {
        public int Index { get; set; }

        public Column(int index)
            : base(HtmlTextWriterTag.Col)
        {
            Index = index;
        }
    }

    public class ColumnGroup : WebControl
    {
        public ColumnGroup(string[] colClasses)
            : base(HtmlTextWriterTag.Colgroup)
        {
            if (colClasses == null)
                throw new ArgumentException("colClasses cannot be null");

            _ColumnNamesMap = new Dictionary<string,int>(colClasses.Length);
            for (var i = 0; i < colClasses.Length; ++i)
            {
                var colClass = colClasses[i] == null ? string.Empty : colClasses[i];
                Controls.Add(new Column(Controls.Count) { CssClass = colClass + " Col" + i.ToString() });
                if (!colClass.IsNullOrEmpty())
                    _ColumnNamesMap.Add(colClass, i);
            }
        }

        public ColumnGroup(int colCount)
            : this(new string[colCount])
        { }

        public ColumnGroup()
            : this(new string[0])
        { }

        public void AddColumn(Column col)
        { Controls.Add(col); }

        public int ColumnCount
        { get { return Controls.Count; } }

        public Column this[int key]
        { get { return (Column)Controls[key]; } }

        public Column this[string key]
        { get { return (Column)Controls[_ColumnNamesMap[key]]; } }

        internal Dictionary<string, int> _ColumnNamesMap;

        public IEnumerable<KeyValuePair<string, int>> ColumnNameIndexMap
        {
            get
            {
                return from k in _ColumnNamesMap.Keys
                       select new KeyValuePair<string, int>(k, _ColumnNamesMap[k]);
            }

            set
            {
                _ColumnNamesMap = null;
                if (value != null)
                {
                    _ColumnNamesMap = new Dictionary<string, int>(value.Count());
                    foreach (var pair in value)
                    {
                        _ColumnNamesMap.Add(pair.Key, pair.Value);
                    }
                }
            }
        }
    }

    public class Table : RowGroup
    {
        private int _RowCount;
        private int _RowGroupCount;

        protected Table(int colCount, string[] colClasses)
            : base(colCount, HtmlTextWriterTag.Table)
        {
            if (colClasses != null)
                _ColumnGroup = new ColumnGroup(colClasses);
        }

        public Table(int columnCount)
            : this(columnCount, null)
        { }

        public Table(string[] columnClasses)
            : this(columnClasses.Length, columnClasses)
        { }

        public ColumnGroup ColumnGroup
        { get { return _ColumnGroup; } set { _ColumnGroup = value; } }

        protected override void _VerifyAdd(Object item)
        {
            base._VerifyAdd(item);
            if (item is Table)
            {
                throw new ArgumentException("TableGen.Table may only be a root element.");
            }
        }

        private void _ApplyColumnClasses(Object item)
        {
            if (item is Row)
            {
                var row = item as Row;
                row.AppendCSSClass("Row" + _RowCount++);
                for (int curCell = 0, curColumn = 0;
                     curCell < row.Count && curColumn < ColumnCount;
                     curColumn += row[curCell].ColumnSpan, ++curCell)
                {
                    if (ColumnGroup != null && row.ApplyColumnStyles)
                        foreach (var cssClass in ColumnGroup[curColumn].CssClass.Split(' '))
                            row[curCell].AppendCSSClass(cssClass);
                }
            }
        }

        protected override void OnItemAdded(Object item, RowGroup owner)
        {
            base.OnItemAdded(item, owner);
            _VerifyAdd(item);
        }

        protected override void OnPreRender(EventArgs e)
        {
            _DepthFirstIterate(this, _ApplyColumnClasses);
            if (ColumnGroup != null)
                Controls.AddAt(0, ColumnGroup);
        }
    }
}
