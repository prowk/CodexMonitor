#ifndef UNICODE
#define UNICODE
#endif
#define _UNICODE
#define _WIN32_WINNT 0x0601
#include <windows.h>
#include <shellapi.h>
#include <wchar.h>
#include <wctype.h>
#include <stdio.h>

// 原生守护进程不加载 CLR/WPF；只枚举当前桌面窗口，不读取对话内容。
static const wchar_t *stopName=L"Local\\CodexMonitor.Stop.1";
static const wchar_t *watcherStopName=L"Local\\CodexMonitor.Watcher.Stop.1";
static const wchar_t *monitorMutex=L"Local\\CodexMonitor.Glass.1";
static wchar_t folder[32768], executable[32768];
typedef struct { BOOL active,probe; unsigned missing; HANDLE stop,child; const wchar_t *eventName; } Session;

static BOOL CALLBACK FindDesktop(HWND hwnd,LPARAM result) {
    if(!IsWindowVisible(hwnd) || GetWindow(hwnd,GW_OWNER))return TRUE;
    wchar_t cls[128];
    if(!GetClassNameW(hwnd,cls,128) || wcsncmp(cls,L"Chrome_WidgetWin_",16)!=0)return TRUE;
    DWORD pid=0;GetWindowThreadProcessId(hwnd,&pid);
    HANDLE process=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,pid);
    if(!process)return TRUE;
    wchar_t path[32768];DWORD length=32768;
    BOOL ok=QueryFullProcessImageNameW(process,0,path,&length);CloseHandle(process);
    if(!ok)return TRUE;
    for(DWORD i=0;i<length;i++)path[i]=towlower(path[i]);
    const wchar_t *name=wcsrchr(path,L'\\');name=name?name+1:path;
    // CLI 和普通 ChatGPT 不属于 Codex 桌面窗口；最小化窗口仍然可见。
    if(wcscmp(name,L"codex.exe")==0 || (wcscmp(name,L"chatgpt.exe")==0 && wcsstr(path,L"\\openai.codex_"))) {
        *(BOOL *)result=TRUE;return FALSE;
    }
    return TRUE;
}
static BOOL DesktopOpen(void){BOOL found=FALSE;EnumWindows(FindDesktop,(LPARAM)&found);return found;}

static BOOL StartMonitor(Session *session) {
    if(!session->probe) {
        HANDLE existing=OpenMutexW(SYNCHRONIZE,FALSE,monitorMutex);
        if(existing){CloseHandle(existing);return TRUE;}
    }
    wchar_t path[32768],command[32768];
    if(session->probe)wcscpy(path,executable);
    else swprintf(path,32768,L"%ls\\CodexMonitor.exe",folder);
    if(session->probe)swprintf(command,32768,L"\"%ls\" --probe-event \"%ls\"",path,session->eventName);
    else swprintf(command,32768,L"\"%ls\"",path);
    STARTUPINFOW startup={0};PROCESS_INFORMATION process={0};startup.cb=sizeof(startup);
    startup.dwFlags=STARTF_USESHOWWINDOW;startup.wShowWindow=SW_HIDE;
    if(!CreateProcessW(path,command,NULL,NULL,FALSE,CREATE_NO_WINDOW,NULL,folder,&startup,&process))return FALSE;
    CloseHandle(process.hThread);
    if(session->child)CloseHandle(session->child);
    session->child=process.hProcess;return TRUE;
}
static void Tick(Session *session,BOOL open) {
    if(open) {
        session->missing=0;
        if(!session->active) {
            // 等上一轮子进程完成清理，避免新一轮打开时竞争单实例互斥量。
            if(session->child && WaitForSingleObject(session->child,0)==WAIT_TIMEOUT)return;
            ResetEvent(session->stop);
            session->active=StartMonitor(session);
        }
    }else if(++session->missing>=3) {
        session->missing=3;SetEvent(session->stop);session->active=FALSE;
    }
}
static int SelfTest(void) {
    wchar_t name[128];swprintf(name,128,L"Local\\CodexMonitor.Test.%lu",GetCurrentProcessId());
    Session s={0};s.probe=TRUE;s.eventName=name;s.stop=CreateEventW(NULL,TRUE,FALSE,name);
    FILE *report=_wfopen(L"lifecycle-results.txt",L"wb");int passed=0;
    if(!s.stop || !report)return 1;
    Tick(&s,FALSE);if(!s.child)passed++;
    Tick(&s,TRUE);if(s.child && WaitForSingleObject(s.child,200)==WAIT_TIMEOUT)passed++;
    HANDLE first=s.child;Tick(&s,TRUE);if(s.child==first)passed++;
    Tick(&s,FALSE);Tick(&s,FALSE);if(WaitForSingleObject(s.child,0)==WAIT_TIMEOUT)passed++;
    Tick(&s,TRUE);if(s.active && s.missing==0)passed++;
    Tick(&s,FALSE);Tick(&s,FALSE);Tick(&s,FALSE);
    if(WaitForSingleObject(s.child,3000)==WAIT_OBJECT_0 && !s.active)passed++;
    Tick(&s,TRUE);if(s.active && WaitForSingleObject(s.child,200)==WAIT_TIMEOUT)passed++;
    // 手动退出后，本次 Codex 会话不反复拉起；下一次打开仍会自动启动。
    SetEvent(s.stop);WaitForSingleObject(s.child,3000);Tick(&s,TRUE);
    if(WaitForSingleObject(s.child,0)==WAIT_OBJECT_0)passed++;
    Tick(&s,FALSE);Tick(&s,FALSE);Tick(&s,FALSE);Tick(&s,TRUE);
    if(WaitForSingleObject(s.child,200)==WAIT_TIMEOUT)passed++;
    SetEvent(s.stop);WaitForSingleObject(s.child,3000);
    fprintf(report,"%s: %d/9 lifecycle checks (real child processes and named events)\n",passed==9?"PASS":"FAIL",passed);
    fclose(report);if(s.child)CloseHandle(s.child);CloseHandle(s.stop);return passed==9?0:1;
}
int WINAPI wWinMain(HINSTANCE instance,HINSTANCE previous,LPWSTR command,int show) {
    (void)instance;(void)previous;(void)command;(void)show;
    GetModuleFileNameW(NULL,executable,32768);wcscpy(folder,executable);
    wchar_t *slash=wcsrchr(folder,L'\\');if(slash)*slash=0;
    int argc=0;wchar_t **argv=CommandLineToArgvW(GetCommandLineW(),&argc);
    if(argc==3 && wcscmp(argv[1],L"--probe-event")==0) {
        HANDLE stop=OpenEventW(SYNCHRONIZE,FALSE,argv[2]);LocalFree(argv);
        if(!stop)return 1;DWORD result=WaitForSingleObject(stop,10000);CloseHandle(stop);return result==WAIT_OBJECT_0?0:1;
    }
    if(argc==2 && wcscmp(argv[1],L"--self-test")==0){LocalFree(argv);return SelfTest();}
    if(argc==2 && wcscmp(argv[1],L"--status")==0) {
        FILE *file=_wfopen(L"watcher-status.txt",L"wb");BOOL open=DesktopOpen();
        if(file){fprintf(file,"codexDesktopOpen=%s\n",open?"True":"False");fclose(file);}
        LocalFree(argv);return open?0:2;
    }
    if(argc==2 && wcscmp(argv[1],L"--stop")==0) {
        HANDLE stop=OpenEventW(EVENT_MODIFY_STATE,FALSE,watcherStopName);
        if(stop){SetEvent(stop);CloseHandle(stop);}LocalFree(argv);return 0;
    }
    LocalFree(argv);
    HANDLE mutex=CreateMutexW(NULL,TRUE,L"Local\\CodexMonitor.Watcher.1");
    if(!mutex)return 1;if(GetLastError()==ERROR_ALREADY_EXISTS){CloseHandle(mutex);return 0;}
    HANDLE quit=CreateEventW(NULL,TRUE,FALSE,watcherStopName);
    Session session={0};session.stop=CreateEventW(NULL,TRUE,FALSE,stopName);
    if(!quit || !session.stop)return 1;
    ResetEvent(quit);
    do { Tick(&session,DesktopOpen()); }while(WaitForSingleObject(quit,1000)==WAIT_TIMEOUT);
    // 停止守护程序时也让显示器正常退出，以便更新和卸载。
    SetEvent(session.stop);if(session.child)CloseHandle(session.child);
    CloseHandle(session.stop);CloseHandle(quit);ReleaseMutex(mutex);CloseHandle(mutex);return 0;
}
