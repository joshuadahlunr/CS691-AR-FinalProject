//
//  ManoProcessor.h
//  ManoSDK
//
//  Created by Julio on 23/04/18.
//  Copyright © 2016 ManoMotion. All rights reserved.
//

#ifndef entry_point_iOS
#define entry_point_iOS

#include "public_structs.h"
#include "BridgeUtils.hpp"
//#include "RenderAPI.h" included in bridgeutils



#define ENTRY_POINT __attribute__ ((visibility ("default")))

extern "C"  {
    
    ENTRY_POINT void init(ManoSettings mano_settings, LicenseStatus* license_status);
    ENTRY_POINT void initSetup(int img_format, AssetStatus *status);
    ENTRY_POINT void initSetupWithKey(ManoSettings settings, AssetStatus* status);

    ENTRY_POINT void processFrame(HandInfo *hand_info0,   Session *manomotion_session);
    
    ENTRY_POINT void processFrameTwoHands(HandInfo *hand_info0,HandInfo *hand_info1,   Session *manomotion_session);

    ENTRY_POINT void  setLeftFrameArray (void * data);
    
    ENTRY_POINT void  setMRFrameArray (void * data);
    ENTRY_POINT void  setMRFrameArrays (void * data0, void * data1);
    ENTRY_POINT int copyHandInfo(HandInfo* first_hand_info, HandInfo* second_hand_info, Session* manomotion_session);
    ENTRY_POINT void  setResolution(int width, int height);
    
    ENTRY_POINT void getPerformanceInfo(int& avg_pt_mm, int& avg_pt_img);
    ENTRY_POINT void  stop();
	ENTRY_POINT  void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API SetTexturesFromUnity(void* textureHandleLeft, void* textureHandleRight, int w, int h, int _splitting_factor);
    ENTRY_POINT  void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API SetTextureFromUnity(void* textureHandleLeft, int w, int h, int _splitting_factor);
    ENTRY_POINT  void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* unityInterfaces);
    ENTRY_POINT  void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload();
    ENTRY_POINT UnityRenderingEvent UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API GetRenderEventFunc(); // to be renamed

    ENTRY_POINT void SetLeftRightFrameArrayPointers(void* _left_TextureHandle, void* _right_TextureHandle, int current_width, int current_height, bool is_async);

}
#endif /* ManoProcessor_h */
