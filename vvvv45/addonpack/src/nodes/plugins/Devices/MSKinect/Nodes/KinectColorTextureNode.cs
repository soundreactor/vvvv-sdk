﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.ComponentModel.Composition;

using SlimDX.Direct3D9;
using SlimDX;
using Microsoft.Kinect;

using VVVV.PluginInterfaces.V2;
using VVVV.PluginInterfaces.V1;
using VVVV.Utils;
using VVVV.MSKinect.Lib;

namespace VVVV.MSKinect.Nodes
{
    [PluginInfo(Name = "RGB", 
        Category = "Kinect",
        Version="Microsoft", 
        Author = "vux", 
        Tags = "EX9, texture",
        Help = "Returns an X8R8G8B8 formatted texture from the Kinects RGB camera")]
    public class KinectColorTextureNode : IPluginEvaluate, IPluginConnections, IPluginDXTexture2
    {
        //memcopy method
        [DllImport("Kernel32.dll", EntryPoint="RtlMoveMemory", SetLastError=false)]
        static extern void CopyMemory(IntPtr dest, IntPtr src, int size);
		
        [Input("Kinect Runtime")]
        private Pin<KinectRuntime> FInRuntime;
		
        [Output("Frame Index",IsSingle=true, Order=10)]
        private ISpread<int> FOutFrameIndex;
		
        private int frameindex = -1;
		
        private IDXTextureOut FOutTexture;
		
        private bool FInvalidateConnect = false;
        private bool FInvalidate = true;
		
        private KinectRuntime runtime;
		
        private byte[] colorimage;
        private object m_colorlock = new object();
		
        private Dictionary<Device, Texture> FColorTex = new Dictionary<Device, Texture>();
		
        [ImportingConstructor()]
        public KinectColorTextureNode(IPluginHost host)
        {
            this.colorimage = new byte[640 * 480 * 4];
            host.CreateTextureOutput("Texture Out", TSliceMode.Single, TPinVisibility.True, out this.FOutTexture);
        }
		
        public void Evaluate(int SpreadMax)
        {
            if (this.FInvalidateConnect)
            {
                if (runtime != null)
                {
                    this.runtime.ColorFrameReady -= ColorFrameReady;
                }
				
                if (this.FInRuntime.PluginIO.IsConnected)
                {
                    //Cache runtime node
                    this.runtime = this.FInRuntime[0];
					
                    if (runtime != null)
                    {
                        this.FInRuntime[0].ColorFrameReady += ColorFrameReady;
                    }
                    
                }
				
                this.FInvalidateConnect = false;
            }
			
            this.FOutFrameIndex[0] = this.frameindex;
        }
		
        public void ConnectPin(IPluginIO pin)
        {
            if (pin == this.FInRuntime.PluginIO)
            {
                this.FInvalidateConnect = true;
            }
        }
		
        public void DisconnectPin(IPluginIO pin)
        {
            if (pin == this.FInRuntime.PluginIO)
            {
                this.FInvalidateConnect = true;
            }
        }
		
        public Texture GetTexture(IDXTextureOut ForPin, Device OnDevice, int Slice)
        {
            if (this.FColorTex.ContainsKey(OnDevice)) 
            { 
                return this.FColorTex[OnDevice]; 
            }
            else
                return null;
        }
		
        public unsafe void UpdateResource(IPluginOut ForPin, Device OnDevice)
        {
            if (this.runtime != null)
            {
                if (!this.FColorTex.ContainsKey(OnDevice))
                {
                    Texture t = null;
                    if (OnDevice is DeviceEx)
                    {
                        t = new Texture(OnDevice, 640, 480, 1, Usage.Dynamic, Format.X8R8G8B8, Pool.Default);
                    }
                    else
                    {
                        t = new Texture(OnDevice, 640, 480, 1, Usage.None, Format.X8R8G8B8, Pool.Managed);
                    }
                    
                    this.FColorTex.Add(OnDevice, t);
                }
				
                if (this.FInvalidate)
                {
                    Texture tx = this.FColorTex[OnDevice];
  
                    //lock the vvvv texture
                    DataRectangle rect;
                    if (tx.Device is DeviceEx)
                        rect = tx.LockRectangle(0, LockFlags.None);
                    else
                        rect = tx.LockRectangle(0, LockFlags.Discard);
                    
                    try
                    {
                        lock (this.m_colorlock)
                        {
                            var row = 640*4;
                            
                            fixed (byte* p = this.colorimage)
                            {
                                IntPtr src = (IntPtr)p;
                                //copy one row a time
                                for (int i = 0; i < 480; i++)
                                { 
                                    CopyMemory(rect.Data.DataPointer.Move(rect.Pitch * i), src, row);
                                    src = src.Move(row);
                                }
                            }
                        }
                    }
                    finally
                    {
                        tx.UnlockRectangle(0);
                    }
						
                    this.FInvalidate = false;
                }
            }
        }
			
        public void DestroyResource(IPluginOut ForPin, Device OnDevice, bool OnlyUnManaged)
        {
            if (this.FColorTex.ContainsKey(OnDevice))
            {
                this.FColorTex[OnDevice].Dispose();
                this.FColorTex.Remove(OnDevice);
            }
        }
			
        private void ColorFrameReady(object sender, ColorImageFrameReadyEventArgs e)
        {
            ColorImageFrame frame = e.OpenColorImageFrame();
				
            if (frame != null)
            {
                this.FInvalidate = true;
                //this.frameindex = frame.FrameNumber;
					
                lock (m_colorlock)
                {
                    frame.CopyPixelDataTo(this.colorimage);
                    //Marshal.Copy(frame..Image.Bits, 0, this.colorimage, 640 * 480 * 4);
                }
                this.FInvalidate = true;
                this.frameindex = frame.FrameNumber;
                
                frame.Dispose();
            }  
        }
    }
}
	