import sys
import json
import os

# Ensure the script runs in the correct directory
current_dir = os.path.dirname(os.path.abspath(__file__))
if current_dir not in sys.path:
    sys.path.insert(0, current_dir)

import server

if sys.stdout.encoding.lower() != 'utf-8':
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except AttributeError:
        pass

def custom_progress(msg):
    print(f"[*] {msg}")

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Sử dụng: python test_triage.py <đường_dẫn_đến_file>")
        print("Ví dụ: python test_triage.py C:\\putty.exe")
        sys.exit(1)
        
    target_file = sys.argv[1]
    print(f"Đang phân tích: {target_file}")
    print("-" * 50)
    
    try:
        # Gọi thẳng hàm _triage_file thay vì chạy qua MCP stdio
        result = server._triage_file(target_file, progress_cb=custom_progress)
        
        print("-" * 50)
        print("KẾT QUẢ TRIAGE (JSON):")
        print(json.dumps(result, indent=4, ensure_ascii=False))
        
        # Test Report generator
        from report_generator import generate_markdown_report
        print("-" * 50)
        print("KẾT QUẢ BÁO CÁO MARKDOWN:")
        print(generate_markdown_report(result))
        
    except Exception as e:
        print(f"Lỗi: {e}")
