﻿namespace AngleSharp.Dom
{
    using AngleSharp.Common;
    using AngleSharp.Css;
    using AngleSharp.Css.Dom;
    using AngleSharp.Css.Parser;
    using AngleSharp.Dom.Events;
    using AngleSharp.Text;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Represents an element node.
    /// </summary>
    class Element : Node, IElement
    {
        #region Fields

        private static readonly ConditionalWeakTable<Element, IShadowRoot> ShadowRoots = new ConditionalWeakTable<Element, IShadowRoot>();
        private static readonly Dictionary<Type, AttrChangedCallback> RegisteredCallbacks = new Dictionary<Type, AttrChangedCallback>();

        private readonly NamedNodeMap _attributes;
        private readonly String _namespace;
        private readonly String _prefix;
        private readonly String _localName;

        private HtmlCollection<IElement> _elements;
        private ICssStyleDeclaration _style;
        private TokenList _classList;

        #endregion

        #region ctor

        static Element()
        {
            RegisterCallback<Element>(AttributeNames.Class, (element, value) => element._classList?.Update(value));
        }

        public Element(Document owner, String localName, String prefix, String namespaceUri, NodeFlags flags = NodeFlags.None)
            : this(owner, prefix != null ? String.Concat(prefix, ":", localName) : localName, localName, prefix, namespaceUri, flags)
        {
        }

        public Element(Document owner, String name, String localName, String prefix, String namespaceUri, NodeFlags flags = NodeFlags.None)
            : base(owner, name, NodeType.Element, flags)
        {
            _localName = localName;
            _prefix = prefix;
            _namespace = namespaceUri;
            _attributes = new NamedNodeMap(this);
        }

        #endregion

        #region Internal Properties

        internal IBrowsingContext Context
        {
            get { return Owner?.Context; }
        }

        internal NamedNodeMap Attributes
        {
            get { return _attributes; }
        }

        #endregion

        #region Properties

        public ICssStyleDeclaration Style
        {
            get { return _style ?? (_style = CreateStyle()); }
        }

        public IElement AssignedSlot
        {
            get 
            { 
                var parent = ParentElement;

                if (parent.IsShadow())
                {
                    var tree = parent.ShadowRoot;
                    return tree.GetAssignedSlot(Slot);
                }

                return null;
            }
        }

        public String Slot
        {
            get { return this.GetOwnAttribute(AttributeNames.Slot); }
            set { this.SetOwnAttribute(AttributeNames.Slot, value); }
        }

        public IShadowRoot ShadowRoot
        {
            get
            {
                var root = default(IShadowRoot);
                ShadowRoots.TryGetValue(this, out root);
                return root;
            }
        }

        public String Prefix
        {
            get { return _prefix; }
        }

        public String LocalName
        {
            get { return _localName; }
        }

        public String NamespaceUri
        {
            get { return _namespace ?? this.GetNamespaceUri(); }
        }

        public override String TextContent
        {
            get
            {
                var sb = StringBuilderPool.Obtain();

                foreach (var child in this.GetDescendants().OfType<IText>())
                {
                    sb.Append(child.Data);
                }

                return sb.ToPool();
            }
            set
            {
                var node = !String.IsNullOrEmpty(value) ? new TextNode(Owner, value) : null;
                ReplaceAll(node, false);
            }
        }

        public ITokenList ClassList
        {
            get
            {
                if (_classList == null)
                {
                    _classList = new TokenList(this.GetOwnAttribute(AttributeNames.Class));
                    _classList.Changed += value => UpdateAttribute(AttributeNames.Class, value);
                }

                return _classList;
            }
        }

        public String ClassName
        {
            get { return this.GetOwnAttribute(AttributeNames.Class); }
            set { this.SetOwnAttribute(AttributeNames.Class, value); }
        }

        public String Id
        {
            get { return this.GetOwnAttribute(AttributeNames.Id); }
            set { this.SetOwnAttribute(AttributeNames.Id, value); }
        }

        public String TagName
        {
            get { return NodeName; }
        }

        public IElement PreviousElementSibling
        {
            get
            {
                var parent = Parent;

                if (parent != null)
                {
                    var found = false;

                    for (var i = parent.ChildNodes.Length - 1; i >= 0; i--)
                    {
                        if (Object.ReferenceEquals(parent.ChildNodes[i], this))
                        {
                            found = true;
                        }
                        else if (found && parent.ChildNodes[i] is IElement)
                        {
                            return (IElement)parent.ChildNodes[i];
                        }
                    }
                }

                return null;
            }
        }

        public IElement NextElementSibling
        {
            get
            {
                var parent = Parent;

                if (parent != null)
                {
                    var n = parent.ChildNodes.Length;
                    var found = false;

                    for (var i = 0; i < n; i++)
                    {
                        if (Object.ReferenceEquals(parent.ChildNodes[i], this))
                        {
                            found = true;
                        }
                        else if (found && parent.ChildNodes[i] is IElement)
                        {
                            return (IElement)parent.ChildNodes[i];
                        }
                    }
                }

                return null;
            }
        }

        public Int32 ChildElementCount
        {
            get
            {
                var children = ChildNodes;
                var n = children.Length;
                var count = 0;

                for (var i = 0; i < n; i++)
                {
                    if (children[i].NodeType == NodeType.Element)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public IHtmlCollection<IElement> Children
        {
            get { return _elements ?? (_elements = new HtmlCollection<IElement>(this, deep: false)); }
        }

        public IElement FirstElementChild
        {
            get 
            {
                var children = ChildNodes;
                var n = children.Length;

                for (var i = 0; i < n; i++)
                {
                    var child = children[i] as IElement;

                    if (child != null)
                    {
                        return child;
                    }
                }

                return null;
            }
        }

        public IElement LastElementChild
        {
            get
            {
                var children = ChildNodes;

                for (int i = children.Length - 1; i >= 0; i--)
                {
                    var child = children[i] as IElement;

                    if (child != null)
                    {
                        return child;
                    }
                }

                return null;
            }
        }

        public String InnerHtml
        {
            get { return ChildNodes.ToHtml(); }
            set { ReplaceAll(new DocumentFragment(this, value), false); }
        }

        public String OuterHtml
        {
            get { return this.ToHtml(); }
            set
            {
                var parent = Parent;

                if (parent == null)
                    throw new DomException(DomError.NotSupported);

                var document = Owner;

                if (document != null && Object.ReferenceEquals(document.DocumentElement, this))
                    throw new DomException(DomError.NoModificationAllowed);

                parent.InsertChild(parent.IndexOf(this), new DocumentFragment(this, value));
                parent.RemoveChild(this);
            }
        }
        
        INamedNodeMap IElement.Attributes
        {
            get { return _attributes; }
        }

        public Boolean IsFocused
        {
            get
            {
                var document = Owner;
                return document != null ? Object.ReferenceEquals(document.FocusElement, this) : false;
            }
            protected set
            {
                var document = Owner;

                if (document != null)
                {
                    if (value)
                    {
                        document.SetFocus(this);
                        this.Fire<FocusEvent>(m => m.Init(EventNames.Focus, false, false));
                    }
                    else
                    {
                        document.SetFocus(null);
                        this.Fire<FocusEvent>(m => m.Init(EventNames.Blur, false, false));
                    }
                }
            }
        }

        #endregion

        #region Methods

        public IShadowRoot AttachShadow(ShadowRootMode mode = ShadowRootMode.Open)
        {
            if (TagNames.AllNoShadowRoot.Contains(_localName))
            {
                throw new DomException(DomError.NotSupported);
            }
            else if (ShadowRoot != null)
            {
                throw new DomException(DomError.InvalidState);
            }

            var root = new ShadowRoot(this, mode);
            ShadowRoots.Add(this, root);
            return root;
        }

        public IElement QuerySelector(String selectors)
        {
            return ChildNodes.QuerySelector(selectors, this);
        }

        public IHtmlCollection<IElement> QuerySelectorAll(String selectors)
        {
            return ChildNodes.QuerySelectorAll(selectors, this);
        }

        public IHtmlCollection<IElement> GetElementsByClassName(String classNames)
        {
            return ChildNodes.GetElementsByClassName(classNames);
        }

        public IHtmlCollection<IElement> GetElementsByTagName(String tagName)
        {
            return ChildNodes.GetElementsByTagName(tagName);
        }

        public IHtmlCollection<IElement> GetElementsByTagNameNS(String namespaceURI, String tagName)
        {
            return ChildNodes.GetElementsByTagName(namespaceURI, tagName);
        }

        public Boolean Matches(String selectorText)
        {
            var sg = Context.GetService<ICssSelectorParser>().ParseSelector(selectorText);

            if (sg == null)
                throw new DomException(DomError.Syntax);

            return sg.Match(this, this);
        }

        public override INode Clone(Boolean deep = true)
        {
            var node = new Element(Owner, LocalName, _prefix, _namespace, Flags);
            CloneElement(node, deep);
            return node;
        }

        public Boolean HasAttribute(String name)
        {
            if (_namespace.Is(NamespaceNames.HtmlUri))
            {
                name = name.HtmlLower();
            }

            return _attributes.GetNamedItem(name) != null;
        }

        public Boolean HasAttribute(String namespaceUri, String localName)
        {
            if (String.IsNullOrEmpty(namespaceUri))
            {
                namespaceUri = null;
            }

            return _attributes.GetNamedItem(namespaceUri, localName) != null;
        }

        public String GetAttribute(String name)
        {
            if (_namespace.Is(NamespaceNames.HtmlUri))
            {
                name = name.HtmlLower();
            }
            
            return _attributes.GetNamedItem(name)?.Value;
        }

        public String GetAttribute(String namespaceUri, String localName)
        {
            if (String.IsNullOrEmpty(namespaceUri))
            {
                namespaceUri = null;
            }
            
            return _attributes.GetNamedItem(namespaceUri, localName)?.Value;
        }

        public void SetAttribute(String name, String value)
        {
            if (value != null)
            {
                if (!name.IsXmlName())
                {
                    throw new DomException(DomError.InvalidCharacter);
                }

                if (_namespace.Is(NamespaceNames.HtmlUri))
                {
                    name = name.HtmlLower();
                }

                this.SetOwnAttribute(name, value);
            }
            else
            {
                RemoveAttribute(name);
            }
        }

        public void SetAttribute(String namespaceUri, String name, String value)
        {
            if (value != null)
            {
                var prefix = default(String);
                var localName = default(String);
                GetPrefixAndLocalName(name, ref namespaceUri, out prefix, out localName);
                _attributes.SetNamedItem(new Attr(prefix, localName, value, namespaceUri));
            }
            else
            {
                RemoveAttribute(namespaceUri, name);
            }
        }

        public Boolean RemoveAttribute(String name)
        {
            if (_namespace.Is(NamespaceNames.HtmlUri))
            {
                name = name.HtmlLower();
            }

            return _attributes.RemoveNamedItemOrDefault(name) != null;
        }

        public Boolean RemoveAttribute(String namespaceUri, String localName)
        {
            if (String.IsNullOrEmpty(namespaceUri))
            {
                namespaceUri = null;
            }

            return _attributes.RemoveNamedItemOrDefault(namespaceUri, localName) != null;
        }

        public void Prepend(params INode[] nodes)
        {
            this.PrependNodes(nodes);
        }

        public void Append(params INode[] nodes)
        {
            this.AppendNodes(nodes);
        }

        public override Boolean Equals(INode otherNode)
        {
            var otherElement = otherNode as IElement;

            if (otherElement != null)
            {
                return NamespaceUri.Is(otherElement.NamespaceUri) &&
                    _attributes.SameAs(otherElement.Attributes) && 
                    base.Equals(otherNode);
            }

            return false;
        }

        public void Before(params INode[] nodes)
        {
            this.InsertBefore(nodes);
        }

        public void After(params INode[] nodes)
        {
            this.InsertAfter(nodes);
        }

        public void Replace(params INode[] nodes)
        {
            this.ReplaceWith(nodes);
        }

        public void Remove()
        {
            this.RemoveFromParent();
        }

        public void Insert(AdjacentPosition position, String html)
        {
            var useThis = position == AdjacentPosition.AfterBegin || position == AdjacentPosition.BeforeEnd;
            var context = useThis ? this : Parent as Element;

            if (context == null)
                throw new DomException("The element has no parent.");

            var nodes = new DocumentFragment(context, html);

            switch (position)
            {
                case AdjacentPosition.BeforeBegin:
                    Parent.InsertBefore(nodes, this);
                    break;

                case AdjacentPosition.AfterEnd:
                    Parent.InsertChild(Parent.IndexOf(this) + 1, nodes);
                    break;

                case AdjacentPosition.AfterBegin:
                    InsertChild(0, nodes);
                    break;

                case AdjacentPosition.BeforeEnd:
                    AppendChild(nodes);
                    break;
            }
        }

        public override void ToHtml(TextWriter writer, IMarkupFormatter formatter)
        {
            var selfClosing = (Flags & NodeFlags.SelfClosing) == NodeFlags.SelfClosing;
            writer.Write(formatter.OpenTag(this, selfClosing));

            if (!selfClosing)
            {
                if (((Flags & NodeFlags.LineTolerance) == NodeFlags.LineTolerance) && FirstChild is IText)
                {
                    var text = (IText)FirstChild;

                    if (text.Data.Has(Symbols.LineFeed))
                    {
                        writer.Write(Symbols.LineFeed);
                    }
                }

                foreach (var child in ChildNodes)
                {
                    child.ToHtml(writer, formatter);
                }
            }

            writer.Write(formatter.CloseTag(this, selfClosing));
        }

        #endregion

        #region Helpers

        internal virtual void SetupElement()
        {
        }

        internal void AttributeChanged(String localName, String namespaceUri, String oldValue, String newValue)
        {
            var callback = GetOrCreateCallback(GetType());

            if (namespaceUri == null && callback != null)
            {
                callback.Invoke(this, localName, newValue);
            }

            Owner.QueueMutation(MutationRecord.Attributes(
                target: this,
                attributeName: localName,
                attributeNamespace: namespaceUri,
                previousValue: oldValue));
        }

        protected ICssStyleDeclaration CreateStyle()
        {
            const NodeFlags TargetFlags = NodeFlags.HtmlMember | NodeFlags.SvgMember;

            if ((Flags & TargetFlags) != NodeFlags.None)
            {
                var context = Context;
                var engine = context.GetCssStyleEngine();

                if (engine != null)
                {
                    var source = this.GetOwnAttribute(AttributeNames.Style);
                    var options = new StyleOptions(context) { Element = this };
                    var style = engine.ParseDeclaration(source, options);
                    style.Changed += value => UpdateAttribute(AttributeNames.Style, value);
                    return style;
                }
            }

            return null;
        }

        protected static void RegisterCallback<TElement>(String name, Action<TElement, String> callback)
            where TElement : Element
        {
            var type = typeof(TElement);
            var existing = GetOrCreateCallback(type);
            var handler = new AttrChangedCallback((element, localName, newValue) =>
            {
                if (localName.Is(name))
                {
                    callback.Invoke((TElement)element, newValue);
                }
            });

            if (existing != null)
            {
                handler = existing + handler;
            }

            RegisteredCallbacks[type] = handler;
        }

        private static AttrChangedCallback GetOrCreateCallback(Type type)
        {
            var handler = default(AttrChangedCallback);

            if (!RegisteredCallbacks.TryGetValue(type, out handler) && type != typeof(Element))
            {
                var parent = type.GetTypeInfo().BaseType;
                handler = GetOrCreateCallback(parent);

                if (handler != null)
                {
                    RegisteredCallbacks[type] = handler;
                }
            }

            return handler;
        }

        protected void UpdateStyle(String value)
        {
            var bindable = _style as IBindable;

            if (String.IsNullOrEmpty(value))
            {
                _attributes.RemoveNamedItemOrDefault(AttributeNames.Style, suppressMutationObservers: true);
            }

            bindable?.Update(value);
        }

        protected void UpdateAttribute(String name, String value)
        {
            this.SetOwnAttribute(name, value, suppressCallbacks: true);
        }

        protected sealed override String LocateNamespace(String prefix)
        {
            return this.LocateNamespaceFor(prefix);
        }

        protected sealed override String LocatePrefix(String namespaceUri)
        {
            return this.LocatePrefixFor(namespaceUri);
        }

        protected void CloneElement(Element element, Boolean deep)
        {
            CloneNode(element, deep);

            foreach (var attribute in _attributes)
            {
                var attr = new Attr(attribute.Prefix, attribute.LocalName, attribute.Value, attribute.NamespaceUri);
                element._attributes.FastAddItem(attr);
            }

            element.SetupElement();
        }
        
        #endregion
    }
}