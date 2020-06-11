﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

using Serilog;

namespace NppXmlTreeviewPlugin.Parsers
{
    /// <summary>
    ///     Represents a Notepadd++ Xml Node.
    /// </summary>
    public class NppXmlNode
    {
        private static int _nodeId = 1;

        /// <summary>
        ///     The start position of the node.
        /// </summary>
        public NppXmlNodePosition StartPosition { get; }

        /// <summary>
        ///     The end position of the node.
        /// </summary>
        public NppXmlNodePosition EndPosition { get; private set; }

        /// <summary>
        ///     The name of the node.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        ///     Internal id for the node.
        /// </summary>
        public int Id { get; }

        /// <summary>
        ///     The childrens of the node.
        /// </summary>
        public IReadOnlyCollection<NppXmlNode> ChildNodes => _childNodes;

        /// <summary>
        ///     The parent of the node.
        /// </summary>
        private NppXmlNode Parent { get; }

        /// <summary>
        ///     Flag to indicate if the node has child nodes.
        /// </summary>
        public bool HasChildNodes => ChildNodes.Any();

        private readonly List<NppXmlNode> _childNodes;

        /// <summary>
        ///     The default constructor for the class.
        /// </summary>
        /// <param name="name">The node name.</param>
        /// <param name="startPosition">The node start position.</param>
        private NppXmlNode(string name, NppXmlNodePosition startPosition)
        {
            Name = name;
            StartPosition = startPosition;
            Id = _nodeId;
            _childNodes = new List<NppXmlNode>();

            _nodeId++;
        }

        /// <summary>
        ///     The default constructor for the class.
        /// </summary>
        /// <param name="name">The node name.</param>
        /// <param name="startPosition">The node start position.</param>
        /// <param name="parent">The parent node.</param>
        private NppXmlNode(string name, NppXmlNodePosition startPosition, NppXmlNode parent)
        {
            Name = name;
            StartPosition = startPosition;
            Parent = parent;
            Id = _nodeId;
            _childNodes = new List<NppXmlNode>();

            _nodeId++;
        }

        /// <summary>
        ///     Returns the first NppXmlNode for the line number.
        /// </summary>
        /// <param name="lineNumber">The line number.</param>
        /// <param name="positionInLine">The position in line.</param>
        /// <returns>The first NppXmlNode in the line.</returns>
        public NppXmlNode FindNppXmlNodeByLine(int lineNumber, int positionInLine)
        {
            if(StartPosition.LineNumber.Equals(lineNumber)
               && StartPosition.LinePosition <= positionInLine
               && EndPosition.LinePosition >= positionInLine)
                return this;

            if(!HasChildNodes)
                return null;

            foreach(var nppXmlNode in ChildNodes)
            {
                var node = nppXmlNode.FindNppXmlNodeByLine(lineNumber, positionInLine);
                if(null != node)
                    return node;
            }

            return null;
        }

        /// <summary>
        ///     Method to try parse the XML.
        /// </summary>
        /// <param name="xml">The XMl as string.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="nppXmlNode">The Notepad++ XmlNode.</param>
        /// <returns>True if parse successfully, false otherwise.</returns>
        public static bool TryParse(string xml, ILogger logger, out NppXmlNode nppXmlNode)
        {
            return TryParse(xml, null, logger, out nppXmlNode);
        }

        /// <summary>
        ///     Method to try parse the XML.
        /// </summary>
        /// <param name="xml">The XMl as string.</param>
        /// <param name="nodeNameAttribute">
        ///     The attribute, that will be used as node name. If no one of that attribute, tag name
        ///     will be used
        /// </param>
        /// <param name="logger">The logger.</param>
        /// <param name="nppXmlNode">The Notepad++ XmlNode.</param>
        /// <returns>True if parse successfully, false otherwise.</returns>
        public static bool TryParse(string xml, string nodeNameAttribute, ILogger logger, out NppXmlNode nppXmlNode)
        {
            nppXmlNode = null;
            _nodeId = 1;

            try
            {
                using(var stringReader = new StringReader(xml))
                {
                    using(var xmlTextReader = new XmlTextReader(stringReader))
                    {
                        while(xmlTextReader.Read())
                        {
                            if(xmlTextReader.NodeType != XmlNodeType.Element || !xmlTextReader.IsStartElement())
                                continue;

                            nppXmlNode = new NppXmlNode(xmlTextReader.Name, new NppXmlNodePosition(xmlTextReader));

                            ReadChildOrSibling(xmlTextReader, xmlTextReader.Depth, nppXmlNode, nodeNameAttribute);
                        }

                        if(null == nppXmlNode)
                            throw new ArgumentException("nppXmlNode");

                        nppXmlNode.EndPosition = new NppXmlNodePosition(xmlTextReader, true);

                        return true;
                    }
                }
            }
            catch(Exception exception)
            {
                logger.Warning(exception, exception.Message);
                nppXmlNode = null;
                return false;
            }
        }

        /// <summary>
        ///     Method to read a child or a sibling of the node.
        /// </summary>
        /// <param name="xmlTextReader">The XML text reader.</param>
        /// <param name="currentDepth">The current depth on the XML tree.</param>
        /// <param name="node">The XML node.</param>
        /// <param name="nodeNameAttribute">The node attribute name.</param>
        private static void ReadChildOrSibling(XmlTextReader xmlTextReader,
                                               int currentDepth,
                                               NppXmlNode node,
                                               string nodeNameAttribute)
        {
            var nodeName = xmlTextReader.Name;
            if(!string.IsNullOrEmpty(nodeNameAttribute))
            {
                var attr = xmlTextReader.GetAttribute(nodeNameAttribute);
                if(attr != null)
                    nodeName = attr;
            }          

            node.Name = nodeName;

            while(xmlTextReader.Read())
            {
                // It's a sibling.
                if(currentDepth == xmlTextReader.Depth)
                {
                    if(xmlTextReader.NodeType != XmlNodeType.Element || !xmlTextReader.IsStartElement())
                        return;

                    var sibling = new NppXmlNode(xmlTextReader.Name,
                        new NppXmlNodePosition(xmlTextReader),
                        node.Parent);
                    node.Parent.AddNode(sibling);

                    // Can be a single node.
                    sibling.EndPosition = new NppXmlNodePosition(xmlTextReader, true);

                    ReadChildOrSibling(xmlTextReader, xmlTextReader.Depth, sibling, nodeNameAttribute);

                    sibling.EndPosition = new NppXmlNodePosition(xmlTextReader, true);

                    return;
                }

                if(xmlTextReader.NodeType != XmlNodeType.Element || !xmlTextReader.IsStartElement())
                    continue;

                // It's a child.
                var child = new NppXmlNode(xmlTextReader.Name, new NppXmlNodePosition(xmlTextReader), node);
                node.AddNode(child);

                // Can be a single node.
                child.EndPosition = new NppXmlNodePosition(xmlTextReader, true);

                ReadChildOrSibling(xmlTextReader, xmlTextReader.Depth, child, nodeNameAttribute);

                child.EndPosition = new NppXmlNodePosition(xmlTextReader, true);
            }
        }

        private void AddNode(NppXmlNode node)
        {
            _childNodes.Add(node);
        }
    }
}
