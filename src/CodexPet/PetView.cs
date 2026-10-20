using System;
using System.Windows;
using System.Windows.Media;
namespace CodexPet;
public enum PetMood { Idle, Blink, Walk, Think, Happy, Rest }
public interface IPetSkin { string Name {get;} void Draw(DrawingContext dc, PetMood mood, int frame); }
// Original code-drawn pixel character: Mint / 薄荷号. No external sprite assets.
public sealed class MintRobot : IPetSkin
{
    readonly System.Collections.Generic.Dictionary<string,SolidColorBrush> brushes=new(); public string Name => "薄荷号";
    public void Draw(DrawingContext d,PetMood mood,int frame)
    {
        void R(string color,double x,double y,double w,double h) { if(!brushes.TryGetValue(color,out var b)) { b=(SolidColorBrush)new BrushConverter().ConvertFromString(color)!;b.Freeze();brushes[color]=b; } d.DrawRectangle(b,null,new Rect(x,y,w,h)); }
        d.PushTransform(new ScaleTransform(4,4));
        var dy=mood==PetMood.Happy ? frame%2 : mood==PetMood.Idle ? (frame/4)%2 : 0;
        d.PushTransform(new TranslateTransform(mood==PetMood.Walk ? (frame%6-3) : 0,dy));
        const string edge="#233A43",mint="#A6E1C5",light="#D9F5E5",dark="#365860";
        R("#30203040",7,29,22,2);
        R(edge,17,1,2,5); R("#F7BA91",16,0,4,3);
        R(edge,6,6,24,17); R(edge,3,10,3,8); R(edge,30,10,3,8);
        R(mint,7,7,22,15); R(light,8,7,20,2); R(dark,9,10,18,9);
        bool shut=mood is PetMood.Blink or PetMood.Rest;
        R(light,12,shut?14:12,3,shut?1:4); R(light,22,shut?14:12,3,shut?1:4);
        if(mood==PetMood.Happy) { R(dark,12,15,3,1); R(dark,22,15,3,1); }
        R("#F7BA91",10,18,3,1); R("#F7BA91",24,18,3,1);
        R(edge,10,23,16,5); R(mint,11,23,14,4); R("#F7BA91",17,24,3,2);
        R(edge,9,mood==PetMood.Walk&&frame%2==0?27:28,6,3); R(edge,22,mood==PetMood.Walk&&frame%2==1?27:28,6,3);
        if(mood==PetMood.Think) for(int i=0;i<3;i++) R(i==frame%3?"#F7BA91":dark,29+i*3,2,2,2);
        if(mood==PetMood.Rest) { R(light,29,2,5,1); R(light,32,3,1,1); R(light,30,4,1,1); R(light,29,5,5,1); }
        d.Pop(); d.Pop();
    }
}
public sealed class PetView : FrameworkElement
{
    public IPetSkin Skin {get;set;}=new MintRobot();
    public PetMood Mood {get;set;}
    public int Frame {get;set;}
    public PetView() { Width=148; Height=132; RenderOptions.SetEdgeMode(this,EdgeMode.Aliased); }
    protected override void OnRender(DrawingContext dc) => Skin.Draw(dc,Mood,Frame);
}

