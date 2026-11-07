// A copy of the player's progress in the iPhone's keychain, called from
// SaveBackup.cs. Unlike the game's normal save (PlayerPrefs), the keychain is
// not cleared when the app is deleted, so a reinstalled game finds the copy
// and carries on where the player left off. The copy also travels with an
// encrypted iCloud or computer backup of the phone, so it comes along to a
// new iPhone set up from that backup. It stays on Apple's devices: nothing is
// sent anywhere by this code.
//
// Written so it compiles the same with or without ARC.
#import <Foundation/Foundation.h>
#import <Security/Security.h>
#include <stdlib.h>
#include <string.h>

static NSMutableDictionary* DriftlineSaveQuery()
{
    NSMutableDictionary* query = [NSMutableDictionary dictionary];
    query[(__bridge id)kSecClass] = (__bridge id)kSecClassGenericPassword;
    query[(__bridge id)kSecAttrService] = @"DriftlineSave";
    query[(__bridge id)kSecAttrAccount] = @"progress";
    return query;
}

extern "C"
{
    // 1 when the copy was stored, 0 when the keychain refused it
    int _Driftline_SaveWrite(const char* text)
    {
        if (text == NULL) return 0;
        @autoreleasepool
        {
            NSData* data = [NSData dataWithBytes:text length:strlen(text)];
            NSMutableDictionary* query = DriftlineSaveQuery();
            NSDictionary* change = @{ (__bridge id)kSecValueData: data };
            OSStatus status = SecItemUpdate((__bridge CFDictionaryRef)query,
                                            (__bridge CFDictionaryRef)change);
            if (status == errSecItemNotFound)
            {
                // readable whenever the phone has been unlocked once since it
                // started - the game only runs then anyway
                query[(__bridge id)kSecAttrAccessible] = (__bridge id)kSecAttrAccessibleAfterFirstUnlock;
                query[(__bridge id)kSecValueData] = data;
                status = SecItemAdd((__bridge CFDictionaryRef)query, NULL);
            }
            return status == errSecSuccess ? 1 : 0;
        }
    }

    // The stored copy (a malloc'd string, freed by Unity), "" when there is
    // no copy, or NULL when the keychain could not be read just now.
    char* _Driftline_SaveRead()
    {
        @autoreleasepool
        {
            NSMutableDictionary* query = DriftlineSaveQuery();
            query[(__bridge id)kSecReturnData] = (__bridge id)kCFBooleanTrue;
            query[(__bridge id)kSecMatchLimit] = (__bridge id)kSecMatchLimitOne;
            CFTypeRef result = NULL;
            OSStatus status = SecItemCopyMatching((__bridge CFDictionaryRef)query, &result);
            if (status == errSecItemNotFound)
            {
                char* none = (char*)malloc(1);
                none[0] = 0;
                return none;
            }
            if (status != errSecSuccess || result == NULL)
            {
                if (result != NULL) CFRelease(result);
                return NULL;
            }
            if (CFGetTypeID(result) != CFDataGetTypeID())
            {
                CFRelease(result);
                return NULL;
            }
            CFDataRef data = (CFDataRef)result;
            CFIndex length = CFDataGetLength(data);
            char* copy = (char*)malloc((size_t)length + 1);
            memcpy(copy, CFDataGetBytePtr(data), (size_t)length);
            copy[length] = 0;
            CFRelease(result);
            return copy;
        }
    }
}
