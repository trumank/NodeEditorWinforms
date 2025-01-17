﻿/*
 * The MIT License (MIT)
 *
 * Copyright (c) 2021 Mariusz Komorowski (komorra)
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), 
 * to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, 
 * and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES 
 * OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE 
 * OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace NodeEditor
{

    public interface INodeType
    {
        string Name { get; }
        Type CustomEditor { get; }
        IEnumerable<Parameter> GetParameters();
        object Invoke(object obj, object[] parameters);
    }

    public enum Direction
    {
        In,
        Out,
    }

    public class Parameter
    {
        public Direction Direction { get; set; }
        public string Name { get; set; }
        public Type ParameterType { get; set; }
    }

    public class MethodNodeType : INodeType
    {
        public MethodInfo Method { get; set; }
        public string Name
        {
            get { return Method.Name; }
        }
        public Type CustomEditor
        {
            get { return null; }
        }
        public IEnumerable<Parameter> GetParameters()
        {
            return Method.GetParameters().Select(p => new Parameter {
                Name = p.Name,
                Direction = p.IsOut ? Direction.Out : Direction.In,
                ParameterType = p.ParameterType,
            });
        }
        public object Invoke(object obj, object[] parameters)
        {
            return Method.Invoke(obj, parameters);
        }
    }
    public class CustomNodeType : INodeType
    {
        public string Name { get; set; }
        public Type CustomEditor  { get; set; }
        public List<Parameter> Parameters { get; set; }
        public IEnumerable<Parameter> GetParameters()
        {
            return Parameters;
        }
        public object Invoke(object obj, object[] parameters)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Class that represents one instance of node.
    /// </summary>
    public class NodeVisual
    {
        public const float NodeWidth = 140;
        public const float HeaderHeight = 20;
        public const float ComponentPadding = 2;

        /// <summary>
        /// Current node name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Current node position X coordinate.
        /// </summary>
        public float X { get; set; }

        /// <summary>
        /// Current node position Y coordinate.
        /// </summary>
        public float Y { get; set; }
        public INodeType Type { get; set; }
        public int Order { get; set; }
        public bool Callable { get; set; }
        public bool ExecInit { get; set; }
        public bool IsSelected { get; set; }
        public FeedbackType Feedback { get; set; }
        private object nodeContext { get; set; } 
        public Control CustomEditor { get; internal set; }
        internal string GUID = Guid.NewGuid().ToString();
        public Color NodeColor = Color.LightCyan;
        public bool IsBackExecuted { get; internal set; }
        private SocketVisual[] socketCache;

        /// <summary>
        /// Tag for various puposes - may be used freely.
        /// </summary>
        public int Int32Tag = 0;
        public string XmlExportName { get; internal set; }

        internal int CustomWidth = -1;
        internal int CustomHeight = -1;

        public NodeVisual()
        {
            Feedback = FeedbackType.Debug;
        }

        public string GetGuid()
        {
            return GUID;
        }

        internal SocketVisual[] GetSockets()
        {
            if(socketCache!=null)
            {
                return socketCache;
            }

            var socketList = new List<SocketVisual>();
            float curInputH = HeaderHeight + ComponentPadding;
            float curOutputH = HeaderHeight + ComponentPadding;

            var NodeWidth = GetNodeBounds().Width;

            if (Callable)
            {
                if (!ExecInit)
                {
                    socketList.Add(new SocketVisual()
                    {
                        Height = SocketVisual.SocketHeight,
                        Name = "Enter",
                        Type = typeof (ExecutionPath),
                        IsMainExecution = true,
                        Width = SocketVisual.SocketHeight,
                        X = X,
                        Y = Y + curInputH,
                        Input = true
                    });
                }
                socketList.Add(new SocketVisual()
                {
                    Height = SocketVisual.SocketHeight,
                    Name = "Exit",
                    IsMainExecution = true,
                    Type = typeof (ExecutionPath),
                    Width = SocketVisual.SocketHeight,
                    X = X + NodeWidth - SocketVisual.SocketHeight,
                    Y = Y + curOutputH
                });
                curOutputH += SocketVisual.SocketHeight + ComponentPadding;
                curInputH += SocketVisual.SocketHeight + ComponentPadding;
            }

            foreach (var input in GetInputs())
            {
                var socket = new SocketVisual();
                socket.Type = input.ParameterType;
                socket.Height = SocketVisual.SocketHeight;
                socket.Name = input.Name;
                socket.Width = SocketVisual.SocketHeight;
                socket.X = X;
                socket.Y = Y + curInputH;
                socket.Input = true;

                socketList.Add(socket);

                curInputH += SocketVisual.SocketHeight + ComponentPadding;
            }
            var ctx = GetNodeContext() as DynamicNodeContext;
            foreach (var output in GetOutputs())
            {
                var socket = new SocketVisual();
                socket.Type = output.ParameterType;
                socket.Height = SocketVisual.SocketHeight;
                socket.Name = output.Name;
                socket.Width = SocketVisual.SocketHeight;
                socket.X = X + NodeWidth - SocketVisual.SocketHeight;
                socket.Y = Y + curOutputH;
                socket.Value = ctx[socket.Name];              
                socketList.Add(socket);

                curOutputH += SocketVisual.SocketHeight + ComponentPadding;
            }

            socketCache = socketList.ToArray();
            return socketCache;
        }

        internal void DiscardCache()
        {
            socketCache = null;
        }

        /// <summary>
        /// Returns node context which is dynamic type. It will contain all node default input/output properties.
        /// </summary>
        public object GetNodeContext()
        {
            const string stringTypeName = "System.String";

            if (nodeContext == null)
            {                
                dynamic context = new DynamicNodeContext();

                foreach (var input in GetInputs())
                {
                    var contextName = input.Name.Replace(" ", "");
                    if (input.ParameterType.FullName.Replace("&", "") == stringTypeName)
                    {
                        context[contextName] = string.Empty;
                    }
                    else
                    {
                        try
                        {
                            context[contextName] = Activator.CreateInstance(AppDomain.CurrentDomain, input.ParameterType.Assembly.GetName().Name,
                            input.ParameterType.FullName.Replace("&", "").Replace(" ", "")).Unwrap();
                        }
                        catch (MissingMethodException ex) //For case when type does not have default constructor
                        {
                            context[contextName] = null;
                        }
                    }
                }
                foreach (var output in GetOutputs())
                {
                    var contextName = output.Name.Replace(" ", "");
                    if (output.ParameterType.FullName.Replace("&", "") == stringTypeName)
                    {
                        context[contextName] = string.Empty;
                    }
                    else
                    {
                        try
                        {
                            context[contextName] = Activator.CreateInstance(AppDomain.CurrentDomain, output.ParameterType.Assembly.GetName().Name,
                            output.ParameterType.FullName.Replace("&", "").Replace(" ", "")).Unwrap();
                        }
                        catch(MissingMethodException ex) //For case when type does not have default constructor
                        {
                            context[contextName] = null;
                        }
                    }
                }

                nodeContext = context;
            }
            return nodeContext;
        }

        public IEnumerable<Parameter> GetInputs()
        {
            return Type.GetParameters().Where(x => x.Direction == Direction.In);
        }

        public IEnumerable<Parameter> GetOutputs()
        {
            return Type.GetParameters().Where(x => x.Direction == Direction.Out);
        }

        /// <summary>
        /// Returns current size of the node.
        /// </summary>        
        public SizeF GetNodeBounds()
        {
            var csize = new SizeF();
            if (CustomEditor != null)
            {
                var zoomable = CustomEditor as IZoomable;
                float zoom = zoomable == null ? 1f : (float)Math.Sqrt(zoomable.Zoom);

                csize = new SizeF(CustomEditor.ClientSize.Width/zoom + 2 + 80 +SocketVisual.SocketHeight*2,
                    CustomEditor.ClientSize.Height/zoom + HeaderHeight + 8);                
            }

            var inputs = GetInputs().Count();
            var outputs = GetOutputs().Count();
            if (Callable)
            {
                inputs++;
                outputs++;
            }
            var h = HeaderHeight + Math.Max(inputs*(SocketVisual.SocketHeight + ComponentPadding),
                outputs*(SocketVisual.SocketHeight + ComponentPadding)) + ComponentPadding*2f;

            csize.Width = Math.Max(csize.Width, NodeWidth);
            csize.Height = Math.Max(csize.Height, h);
            if(CustomWidth >= 0)
            {
                csize.Width = CustomWidth;
            }
            if(CustomHeight >= 0)
            {
                csize.Height = CustomHeight;
            }

            return new SizeF(csize.Width, csize.Height);
        }

        /// <summary>
        /// Returns current size of node caption (header belt).
        /// </summary>
        /// <returns></returns>
        public SizeF GetHeaderSize()
        {
            return new SizeF(GetNodeBounds().Width, HeaderHeight);
        }

        private static Font font = SystemFonts.DefaultFont;

        /// <summary>
        /// Allows node to be drawn on given Graphics context.       
        /// </summary>
        /// <param name="g">Graphics context.</param>
        /// <param name="mouseLocation">Location of the mouse relative to NodesControl instance.</param>
        /// <param name="mouseButtons">Mouse buttons that are pressed while drawing node.</param>
        public void Draw(GLGraphics g, RectangleF clipBounds, PointF mouseLocation, MouseButtons mouseButtons)
        {
            var rect = new RectangleF(new PointF(X,Y), GetNodeBounds());
            if (!rect.IntersectsWith(clipBounds)) return;

            var feedrect = rect;
            feedrect.Inflate(10, 10);

            if (Feedback == FeedbackType.Warning)
            {
                g.DrawRectangle(new Pen(Color.Yellow, 4), feedrect);
            }
            else if (Feedback == FeedbackType.Error)
            {
                g.DrawRectangle(new Pen(Color.Red, 5), feedrect);
            }

            var caption = new RectangleF(new PointF(X,Y), GetHeaderSize());
            bool mouseHoverCaption = caption.Contains(mouseLocation);

            g.FillRectangle(new SolidBrush(NodeColor), rect);

            if (IsSelected)
            {
                g.FillRectangle(new SolidBrush(Color.FromArgb(180,Color.WhiteSmoke)), rect);
                g.FillRectangle(mouseHoverCaption ? Brushes.Gold : Brushes.Goldenrod, caption);
            }
            else
            {
                g.FillRectangle(mouseHoverCaption ? Brushes.Cyan : Brushes.Aquamarine, caption);
            }
            g.DrawRectangle(Pens.Gray, caption);
            g.DrawRectangle(Pens.Black, rect);

            // TODO why tf is SystemFonts.DefaultFont so slow
            g.DrawString(Name, font, Brushes.Black, new PointF(X + 3, Y + 3));

            var sockets = GetSockets();
            foreach (var socet in sockets)
            {
                socet.Draw(g, mouseLocation, mouseButtons);
            }
        }

        internal void Execute(INodesContext context)
        {
            context.CurrentProcessingNode = this;

            var dc = (GetNodeContext() as DynamicNodeContext);
            var parametersDict = Type.GetParameters().ToDictionary(x => x.Name, x => dc[x.Name]);
            var parameters = parametersDict.Values.ToArray();

            int ndx = 0;
            Type.Invoke(context, parameters);
            foreach (var kv in parametersDict.ToArray())
            {
                parametersDict[kv.Key] = parameters[ndx];
                ndx++;
            }

            var outs = GetSockets();

            
            foreach (var parameter in dc.ToArray())
            {
                dc[parameter] = parametersDict[parameter];
                var o = outs.FirstOrDefault(x => x.Name == parameter);
                //if (o != null)
                Debug.Assert(o != null, "Output not found");
                {
                    o.Value = dc[parameter];
                }                                
            }
        }

        public void LayoutEditor(float zoom)
        {
            if (CustomEditor != null)
            {
                CustomEditor.Location = new Point((int)(zoom * (X + 1 + 40 + SocketVisual.SocketHeight)), (int)(zoom * (Y + HeaderHeight + 4)));
            }
        }
    }
}
