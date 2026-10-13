// Apple's "allow tracking?" popup (App Tracking Transparency), called from
// TrackingPrompt.cs. iOS 14 and later; older versions have no popup.
#import <Foundation/Foundation.h>
#import <AppTrackingTransparency/AppTrackingTransparency.h>

extern "C"
{
    // 0 not asked yet, 1 restricted, 2 denied, 3 allowed
    int _Driftline_TrackingStatus()
    {
        if (@available(iOS 14, *))
            return (int)[ATTrackingManager trackingAuthorizationStatus];
        return 3;
    }

    void _Driftline_RequestTracking()
    {
        if (@available(iOS 14, *))
        {
            dispatch_async(dispatch_get_main_queue(), ^{
                [ATTrackingManager requestTrackingAuthorizationWithCompletionHandler:
                    ^(ATTrackingManagerAuthorizationStatus status) { }];
            });
        }
    }
}
