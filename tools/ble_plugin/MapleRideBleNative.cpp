// MapleRideBleNative — native C++/WinRT heart-rate monitor for MapleRide.
//
// WHY NATIVE: Unity's desktop Mono cannot load the WinRT "Windows" metadata assembly, so a MANAGED
// DLL that references Windows.Devices.Bluetooth throws TypeLoadException at load. A native DLL that
// uses C++/WinRT internally and exposes plain C functions sidesteps that entirely — Mono P/Invokes
// the C exports and never sees a WinRT type.
//
// Standard Heart Rate Service (0x180D) / Heart Rate Measurement (0x2A37). Finds the strap two ways:
// paired-device enumeration (bonded straps do not advertise) AND advertisement watching (matched on
// the HR service uuid or the name filter). Values are read from the game thread via the C getters.

#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Devices.Enumeration.h>
#include <winrt/Windows.Devices.Bluetooth.h>
#include <winrt/Windows.Devices.Bluetooth.Advertisement.h>
#include <winrt/Windows.Devices.Bluetooth.GenericAttributeProfile.h>
#include <winrt/Windows.Storage.Streams.h>

#include <atomic>
#include <mutex>
#include <set>
#include <string>
#include <vector>
#include <chrono>
#include <cwctype>

using namespace winrt;
using namespace Windows::Foundation;
using namespace Windows::Foundation::Collections;
using namespace Windows::Devices::Bluetooth;
using namespace Windows::Devices::Bluetooth::Advertisement;
using namespace Windows::Devices::Bluetooth::GenericAttributeProfile;
using namespace Windows::Devices::Enumeration;
using namespace Windows::Storage::Streams;

namespace {

std::atomic<int>    g_bpm{ 0 };
std::atomic<int>    g_adv{ 0 };
std::atomic<bool>   g_haveMeasurement{ false };
std::atomic<double> g_lastBeat{ 0.0 };
std::atomic<int>    g_connecting{ 0 };
double              g_timeout = 5.0;
bool                g_apartment = false;

std::mutex          g_mx;
std::wstring        g_status = L"idle";
std::wstring        g_seen;
std::wstring        g_deviceName;
std::wstring        g_nameFilter;
std::set<std::wstring> g_seenSet;

BluetoothLEAdvertisementWatcher g_watcher{ nullptr };
BluetoothLEDevice               g_device{ nullptr };
GattCharacteristic              g_measurement{ nullptr };
event_token g_rxTok{};
event_token g_valTok{};
event_token g_connTok{};

double steady_now()
{
    return std::chrono::duration<double>(std::chrono::steady_clock::now().time_since_epoch()).count();
}

void set_status(std::wstring s)
{
    std::lock_guard<std::mutex> l(g_mx);
    g_status = std::move(s);
}

void add_seen(std::wstring name)
{
    if (name.empty()) name = L"(unnamed)";
    std::lock_guard<std::mutex> l(g_mx);
    if (!g_seenSet.insert(name).second) return;
    g_seen.clear();
    int n = 0;
    for (auto const& x : g_seenSet)
    {
        if (n++ > 0) g_seen += L", ";
        g_seen += x;
        if (n >= 8) { g_seen += L", ..."; break; }
    }
}

bool contains_ci(std::wstring hay, std::wstring needle)
{
    auto lower = [](std::wstring& s) { for (auto& c : s) c = (wchar_t)towlower(c); };
    lower(hay); lower(needle);
    return needle.empty() || hay.find(needle) != std::wstring::npos;
}

int parse(std::vector<uint8_t> const& d)
{
    if (d.size() < 2) return -1;
    bool sixteen = (d[0] & 0x01) != 0;
    if (sixteen)
    {
        if (d.size() < 3) return -1;
        return d[1] | (d[2] << 8);
    }
    return d[1];
}

int copy_str(std::wstring const& s, wchar_t* buf, int len)
{
    if (!buf || len <= 0) return (int)s.size();
    int n = (int)((size_t)(len - 1) < s.size() ? (size_t)(len - 1) : s.size());
    if (n > 0) wmemcpy(buf, s.c_str(), n);
    buf[n] = L'\0';
    return (int)s.size();
}

void on_value(GattCharacteristic const&, GattValueChangedEventArgs const& args)
{
    try
    {
        auto buf = args.CharacteristicValue();
        uint32_t len = buf.Length();
        if (len == 0) return;
        auto reader = DataReader::FromBuffer(buf);
        std::vector<uint8_t> bytes(len);
        reader.ReadBytes(bytes);
        int bpm = parse(bytes);
        if (bpm < 0) return;
        g_bpm = bpm;
        g_lastBeat.store(steady_now());
    }
    catch (...) {}
}

fire_and_forget connect_device(BluetoothLEDevice dev)
{
    try
    {
        g_device = dev;
        { std::lock_guard<std::mutex> l(g_mx); g_deviceName = std::wstring(dev.Name().c_str()); }

        g_connTok = dev.ConnectionStatusChanged([](BluetoothLEDevice const& s, IInspectable const&)
        {
            if (s.ConnectionStatus() != BluetoothConnectionStatus::Disconnected) return;
            g_haveMeasurement = false;
            g_lastBeat.store(0.0);
            g_connecting = 0;
            set_status(L"disconnected");
            try { if (g_watcher) { g_watcher.Start(); set_status(L"scanning"); } } catch (...) {}
        });

        auto svc = co_await dev.GetGattServicesForUuidAsync(GattServiceUuids::HeartRate(), BluetoothCacheMode::Uncached);
        if (svc.Status() != GattCommunicationStatus::Success || svc.Services().Size() == 0)
        { set_status(L"no service"); g_connecting = 0; co_return; }

        auto chr = co_await svc.Services().GetAt(0).GetCharacteristicsForUuidAsync(
            GattCharacteristicUuids::HeartRateMeasurement(), BluetoothCacheMode::Uncached);
        if (chr.Status() != GattCommunicationStatus::Success || chr.Characteristics().Size() == 0)
        { set_status(L"no characteristic"); g_connecting = 0; co_return; }

        g_measurement = chr.Characteristics().GetAt(0);
        g_valTok = g_measurement.ValueChanged(on_value);
        g_haveMeasurement = true;

        auto st = co_await g_measurement.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue::Notify);
        set_status(st == GattCommunicationStatus::Success ? L"connected" : L"subscribe failed");
    }
    catch (hresult_error const& e)
    {
        set_status(std::wstring(L"error: ") + e.message().c_str());
        g_connecting = 0;
    }
    catch (...) { set_status(L"connect error"); g_connecting = 0; }
}

fire_and_forget connect_by_address(uint64_t address)
{
    try
    {
        auto dev = co_await BluetoothLEDevice::FromBluetoothAddressAsync(address);
        if (!dev) { set_status(L"no device"); g_connecting = 0; co_return; }
        connect_device(dev);
    }
    catch (...) { set_status(L"device error"); g_connecting = 0; }
}

fire_and_forget try_paired()
{
    try
    {
        auto selector = GattDeviceService::GetDeviceSelectorFromUuid(GattServiceUuids::HeartRate());
        auto list = co_await DeviceInformation::FindAllAsync(selector);
        for (auto const& di : list)
        {
            add_seen(std::wstring(di.Name().c_str()));
            std::wstring nf; { std::lock_guard<std::mutex> l(g_mx); nf = g_nameFilter; }
            bool nameOk = nf.empty() || contains_ci(std::wstring(di.Name().c_str()), nf);
            if (!nameOk) continue;
            if (g_connecting.exchange(1) != 0) co_return;
            set_status(L"connecting (paired)");
            auto dev = co_await BluetoothLEDevice::FromIdAsync(di.Id());
            if (dev) connect_device(dev);
            else { set_status(L"no device"); g_connecting = 0; }
            co_return;
        }
    }
    catch (...) { set_status(L"enum error"); }
}

void on_advertisement(BluetoothLEAdvertisementWatcher const& sender,
                      BluetoothLEAdvertisementReceivedEventArgs const& args)
{
    g_adv.fetch_add(1);
    std::wstring name = std::wstring(args.Advertisement().LocalName().c_str());

    bool advHr = false;
    for (auto const& u : args.Advertisement().ServiceUuids())
        if (u == GattServiceUuids::HeartRate()) { advHr = true; break; }

    add_seen(name.empty() ? (advHr ? std::wstring(L"(unnamed HR strap)") : std::wstring()) : name);

    std::wstring nf; { std::lock_guard<std::mutex> l(g_mx); nf = g_nameFilter; }
    bool nameMatch = !nf.empty() && !name.empty() && contains_ci(name, nf);
    if (!(nameMatch || advHr)) return;

    if (g_connecting.exchange(1) != 0) return;
    try { sender.Stop(); } catch (...) {}
    set_status(L"connecting");
    connect_by_address(args.BluetoothAddress());
}

} // namespace

extern "C" {

__declspec(dllexport) void mrhr_stop()
{
    try
    {
        if (g_watcher)
        {
            try { g_watcher.Received(g_rxTok); } catch (...) {}
            try { g_watcher.Stop(); } catch (...) {}
            g_watcher = nullptr;
        }
        if (g_measurement)
        {
            try { g_measurement.ValueChanged(g_valTok); } catch (...) {}
            g_measurement = nullptr;
        }
        if (g_device)
        {
            try { g_device.ConnectionStatusChanged(g_connTok); } catch (...) {}
            try { g_device.Close(); } catch (...) {}
            g_device = nullptr;
        }
    }
    catch (...) {}
    g_haveMeasurement = false;
    set_status(L"idle");
}

__declspec(dllexport) void mrhr_start(const wchar_t* nameFilter, double timeout)
{
    mrhr_stop();

    if (!g_apartment)
    {
        try { init_apartment(apartment_type::multi_threaded); g_apartment = true; }
        catch (...) { /* thread already has an apartment; WinRT calls still work */ }
    }

    {
        std::lock_guard<std::mutex> l(g_mx);
        g_nameFilter = nameFilter ? nameFilter : L"";
        // trim
        while (!g_nameFilter.empty() && iswspace(g_nameFilter.front())) g_nameFilter.erase(g_nameFilter.begin());
        while (!g_nameFilter.empty() && iswspace(g_nameFilter.back())) g_nameFilter.pop_back();
        g_seen.clear();
        g_seenSet.clear();
    }
    g_timeout = timeout < 1.0 ? 1.0 : timeout;
    g_connecting = 0;
    g_adv = 0;
    g_bpm = 0;
    g_lastBeat.store(0.0);
    g_haveMeasurement = false;
    set_status(L"scanning");

    try
    {
        // Path 1: already-paired straps (they do not advertise while bonded).
        try_paired();

        // Path 2: advertising straps.
        g_watcher = BluetoothLEAdvertisementWatcher();
        g_watcher.ScanningMode(BluetoothLEScanningMode::Active);
        g_rxTok = g_watcher.Received(on_advertisement);
        g_watcher.Start();
    }
    catch (hresult_error const& e)
    {
        set_status(std::wstring(L"start error: ") + e.message().c_str());
    }
    catch (...) { set_status(L"start error"); }
}

__declspec(dllexport) int mrhr_bpm() { return g_bpm.load(); }

__declspec(dllexport) int mrhr_connected()
{
    if (!g_haveMeasurement.load()) return 0;
    double lb = g_lastBeat.load();
    if (lb <= 0.0) return 0;
    return (steady_now() - lb) <= g_timeout ? 1 : 0;
}

__declspec(dllexport) int mrhr_adv_count() { return g_adv.load(); }

__declspec(dllexport) int mrhr_status(wchar_t* buf, int len)
{
    std::lock_guard<std::mutex> l(g_mx);
    return copy_str(g_status, buf, len);
}

__declspec(dllexport) int mrhr_seen(wchar_t* buf, int len)
{
    std::lock_guard<std::mutex> l(g_mx);
    return copy_str(g_seen, buf, len);
}

__declspec(dllexport) int mrhr_device(wchar_t* buf, int len)
{
    std::lock_guard<std::mutex> l(g_mx);
    return copy_str(g_deviceName, buf, len);
}

} // extern "C"
