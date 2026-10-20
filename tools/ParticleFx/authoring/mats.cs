using UnityEngine;
public class Script { public static object Main(){
  UnityEditor.AssetDatabase.Refresh();
  FxKit.MakeTextures();
  FxKit.Mat("M_Flash_Add","T_SoftCircle",2,new Color(3f,3f,3f,1));
  FxKit.Mat("M_Glow_Add","T_SoftCircle",2,new Color(1.2f,1.2f,1.2f,1));
  FxKit.Mat("M_Spark_Add","T_Spark",2,new Color(2.5f,2.5f,2.5f,1));
  FxKit.Mat("M_Ember_Add","T_Ember",2,new Color(2f,2f,2f,1));
  FxKit.Mat("M_Fire_Add","T_Smoke",2,new Color(1.6f,1.6f,1.6f,1));
  FxKit.Mat("M_Smoke_Lit","T_Smoke",0,Color.white,true);
  FxKit.Mat("M_Debris","T_Debris",0,Color.white);
  FxKit.Mat("M_Ring_Add","T_Ring",2,new Color(1.5f,1.5f,1.5f,1));
  FxKit.Mat("M_Ring_Alpha","T_Ring",0,Color.white);
  FxKit.Mat("M_Bolt_Add","T_Bolt",2,new Color(1.8f,1.8f,1.8f,1));
  FxKit.Mat("M_Trail_Smoke","T_SoftCircle",0,Color.white);
  UnityEditor.AssetDatabase.SaveAssets();
  return "mats ok";
} }
