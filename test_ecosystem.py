import os
import sys
import ctypes
from ctypes import c_char_p, c_void_p

def test_lief(target_file):
    print(f"\n[+] Testing LIEF on {target_file}")
    try:
        import lief
        parsed = lief.parse(target_file)
        if parsed:
            print(f"    - Type: {parsed.format}")
            if hasattr(parsed, 'entrypoint'):
                print(f"    - Entrypoint: {hex(parsed.entrypoint)}")
            print("    [OK] LIEF is working!")
        else:
            print("    [FAIL] LIEF could not parse the file.")
    except Exception as e:
        print(f"    [ERROR] LIEF error: {e}")

def test_binwalk(target_file, lib_dir):
    print(f"\n[+] Testing Binwalk on {target_file}")
    try:
        os.environ['PATH'] = lib_dir + os.pathsep + os.environ['PATH']
        binwalk_dll = ctypes.CDLL(os.path.join(lib_dir, "binwalk_core.dll"))
        
        # Setup binwalk_scan signature
        binwalk_scan = binwalk_dll.binwalk_scan
        binwalk_scan.argtypes = [c_char_p]
        binwalk_scan.restype = c_void_p
        
        # Setup binwalk_free signature
        binwalk_free = binwalk_dll.binwalk_free
        binwalk_free.argtypes = [c_void_p]
        binwalk_free.restype = None
        
        target_bytes = target_file.encode('utf-8')
        ptr = binwalk_scan(target_bytes)
        
        if ptr:
            json_result = ctypes.cast(ptr, c_char_p).value.decode('utf-8')
            print(f"    - Binwalk Output: {json_result[:200]}...")
            binwalk_free(ptr)
            print("    [OK] Binwalk is working!")
        else:
            print("    [FAIL] Binwalk returned null.")
    except Exception as e:
        print(f"    [ERROR] Binwalk error: {e}")

def test_die(target_file, lib_dir):
    print(f"\n[+] Testing Detect-It-Easy (DIE) on {target_file}")
    try:
        # Load die_core.dll and all its Qt dependencies
        os.environ['PATH'] = lib_dir + os.pathsep + os.environ['PATH']
        die_dll = ctypes.CDLL(os.path.join(lib_dir, "die_core.dll"))
        
        die_load = die_dll.DIE_LoadDatabaseA
        die_load.argtypes = [c_char_p]
        die_load.restype = ctypes.c_int
        
        die_scan = die_dll.DIE_ScanFileA
        die_scan.argtypes = [c_char_p, ctypes.c_uint32, c_char_p]
        die_scan.restype = c_void_p
        
        die_free = die_dll.DIE_FreeMemoryA
        die_free.argtypes = [c_void_p]
        die_free.restype = None
        
        target_bytes = target_file.encode('utf-8')
        rules_dir = os.path.join(current_dir, "Rules")
        db_path = os.path.join(rules_dir, "die_db").encode('utf-8')
        
        # Load the DB
        status = die_load(db_path)
        print(f"    - DB Load Status: {status}")
            
        # DIE_ScanFileA(pszFileName, nFlags, pszDatabase)
        ptr = die_scan(target_bytes, 0, db_path)
        
        if ptr:
            result = ctypes.cast(ptr, c_char_p).value.decode('utf-8')
            print(f"    - DIE Output:\n{result}")
            die_free(ptr)
            print("    [OK] DIE is working!")
        else:
            print("    [FAIL] DIE returned null. It might need a database loaded.")
    except Exception as e:
        print(f"    [ERROR] DIE error: {e}")

if __name__ == "__main__":
    current_dir = os.path.dirname(os.path.abspath(__file__))
    lib_dir = os.path.join(current_dir, "lib")
    
    # Let's test on the portable Python executable itself
    target = os.path.join(current_dir, "Python_Runtime", "python.exe")
    
    if not os.path.exists(target):
        print(f"Error: Target {target} not found!")
        sys.exit(1)
        
    print(f"=====================================")
    print(f" RE ECOSYSTEM 2.0 - INTEGRATION TEST")
    print(f"=====================================")
    test_lief(target)
    test_binwalk(target, lib_dir)
    test_die(target, lib_dir)
