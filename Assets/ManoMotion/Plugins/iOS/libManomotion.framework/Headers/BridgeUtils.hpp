#pragma once
#include "RenderAPI.h"
#include "public_structs.h"
#if defined(WIN32)
	#include "WindowsAdapter.h"
#elif defined(__ANDROID__) 
	#include "AdapterUnityAndroid.h"
#elif defined(_IS_IOS_)
    #include "ios/entry-point/native/AdapterIOSNative.hpp"
#endif

//#include "private_structs.h"
//#include "ManoLogger.h"

class BridgeUtils {
private:

public:
	static void copyHandInfo(HandInfo* src_hand_info, HandInfo* dst_hand_info, bool is_data_updated);
	static void resizeFrame(cv::Mat& input, cv::Mat& output, int new_width, int new_height);

	static void retrieveFrameFromTexture(void* textureHandle, cv::Mat& output_img, int _new_width, int _new_height, RenderAPI* s_CurrentAPI);
	// Unified signature that works for all platforms
    static void retrieveFramesFromTextures(void* left_TextureHandle, 
                                         void* right_TextureHandle, 
                                         int _width, 
                                         int _height, 
                                         void* adapter, 
                                         RenderAPI* s_CurrentAPI);
	
};

