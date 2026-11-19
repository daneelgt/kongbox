package dev.kongbox.panel

import android.Manifest
import android.annotation.SuppressLint
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothClass
import android.bluetooth.BluetoothSocket
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import android.util.Base64
import org.json.JSONObject
import java.io.BufferedInputStream
import java.io.DataInputStream
import java.io.DataOutputStream
import java.io.InputStream
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger
import kotlin.concurrent.thread

/**
 * Bluetooth route: the WebView loads http://127.0.0.1:8768 (a tiny HTTP proxy on the phone); each request travels
 * to the PC as a length-prefixed JSON frame over one RFCOMM socket and the answer comes back the same way.
 * The PC must be paired in Android's Bluetooth settings (and the phone in Windows'); the link is encrypted.
 */
class BtBridge(private val context: Context) {
    companion object {
        const val PORT = 8768
        const val PANEL = "http://127.0.0.1:$PORT/panel/"
        val SERVICE: UUID = UUID.fromString("6c1f2a8e-4b1d-4f6a-9a2e-6b6f6e67626f")
    }

    private val prefs = context.getSharedPreferences("kongbox", Context.MODE_PRIVATE)
    private val nextId = AtomicInteger(1)
    private val waiting = ConcurrentHashMap<Int, LinkedBlockingQueue<JSONObject>>()
    private val writeLock = Any()
    @Volatile private var socket: BluetoothSocket? = null
    @Volatile private var out: DataOutputStream? = null
    private var server: ServerSocket? = null

    @Suppress("DEPRECATION")
    private val adapter: BluetoothAdapter? get() = BluetoothAdapter.getDefaultAdapter()

    fun permitted(): Boolean = Build.VERSION.SDK_INT < 31 ||
        context.checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED

    val connected: Boolean get() = socket?.isConnected == true

    /** Paired computers first (and the last one that worked before everything else). */
    @SuppressLint("MissingPermission")
    private fun candidates(): List<android.bluetooth.BluetoothDevice> {
        val bonded = adapter?.bondedDevices?.toList() ?: return emptyList()
        val last = prefs.getString("btAddress", null)
        return bonded.filter { it.bluetoothClass?.majorDeviceClass == BluetoothClass.Device.Major.COMPUTER || it.address == last }
            .sortedByDescending { it.address == last }
    }

    /** Blocking: opens the RFCOMM link to the first paired PC that runs KONGBOX. Call off the main thread. */
    @SuppressLint("MissingPermission")
    @Synchronized
    fun connect(): Boolean {
        if (connected) return true
        val a = adapter ?: return false
        if (!a.isEnabled || !permitted()) return false
        try { a.cancelDiscovery() } catch (_: Exception) { }
        for (device in candidates()) {
            try {
                val s = device.createRfcommSocketToServiceRecord(SERVICE)
                s.connect()
                socket = s
                out = DataOutputStream(s.outputStream)
                prefs.edit().putString("btAddress", device.address).apply()
                startReader(s.inputStream)
                startProxy()
                return true
            } catch (_: Exception) { /* not this one */ }
        }
        return false
    }

    private fun drop() {
        try { socket?.close() } catch (_: Exception) { }
        socket = null
        out = null
        // wake every waiting request so the WebView gets an error instead of hanging
        waiting.values.forEach { it.offer(JSONObject().put("s", 502)) }
    }

    private fun startReader(input: InputStream) {
        thread(isDaemon = true, name = "bt-reader") {
            val din = DataInputStream(BufferedInputStream(input))
            try {
                while (true) {
                    val len = din.readInt()
                    if (len <= 0 || len > 8_000_000) break
                    val buf = ByteArray(len)
                    din.readFully(buf)
                    val res = JSONObject(String(buf, Charsets.UTF_8))
                    waiting.remove(res.optInt("i"))?.offer(res)
                }
            } catch (_: Exception) { }
            drop()
        }
    }

    /** One request over the link; returns {s, t, b} or null on timeout. */
    fun request(method: String, path: String, headers: Map<String, String>, body: ByteArray?): JSONObject? {
        val o = out ?: return null
        val id = nextId.getAndIncrement()
        val q = LinkedBlockingQueue<JSONObject>(1)
        waiting[id] = q
        val h = JSONObject()
        headers.forEach { (k, v) -> h.put(k, v) }
        val req = JSONObject().put("i", id).put("m", method).put("p", path).put("h", h)
        if (body != null && body.isNotEmpty()) req.put("b", Base64.encodeToString(body, Base64.NO_WRAP))
        val bytes = req.toString().toByteArray(Charsets.UTF_8)
        try {
            synchronized(writeLock) { o.writeInt(bytes.size); o.write(bytes); o.flush() }
        } catch (_: Exception) { waiting.remove(id); drop(); return null }
        val res = q.poll(20, TimeUnit.SECONDS)
        waiting.remove(id)
        return res
    }

    /** The local HTTP side the WebView talks to (loopback only). */
    @Synchronized
    private fun startProxy() {
        if (server != null) return
        val ss = ServerSocket(PORT, 32, InetAddress.getByName("127.0.0.1"))
        server = ss
        thread(isDaemon = true, name = "bt-proxy") {
            while (true) {
                val c = try { ss.accept() } catch (_: Exception) { break }
                thread(isDaemon = true) { handle(c) }
            }
        }
    }

    private fun handle(c: Socket) {
        c.use { sock ->
            try {
                sock.soTimeout = 25_000
                val input = BufferedInputStream(sock.getInputStream())
                val requestLine = readLine(input) ?: return
                val parts = requestLine.split(' ')
                if (parts.size < 2) return
                val headers = HashMap<String, String>()
                while (true) {
                    val line = readLine(input) ?: break
                    if (line.isEmpty()) break
                    val i = line.indexOf(':')
                    if (i > 0) headers[line.substring(0, i).trim()] = line.substring(i + 1).trim()
                }
                val length = headers.entries.firstOrNull { it.key.equals("Content-Length", true) }?.value?.toIntOrNull() ?: 0
                val body = if (length > 0) ByteArray(length).also { DataInputStream(input).readFully(it) } else null
                val forward = HashMap<String, String>()
                headers.entries.firstOrNull { it.key.equals("Content-Type", true) }?.let { forward["Content-Type"] = it.value }
                headers.entries.firstOrNull { it.key.equals("X-Kong", true) }?.let { forward["X-Kong"] = it.value }
                val res = request(parts[0], parts[1], forward, body)
                val status = res?.optInt("s", 502) ?: 504
                val data = res?.optString("b")?.takeIf { it.isNotEmpty() }?.let { Base64.decode(it, Base64.DEFAULT) } ?: ByteArray(0)
                val type = res?.optString("t")?.takeIf { it.isNotEmpty() } ?: "text/plain"
                val head = "HTTP/1.1 $status OK\r\nContent-Type: $type\r\nContent-Length: ${data.size}\r\nCache-Control: no-cache\r\nConnection: close\r\n\r\n"
                val os = sock.getOutputStream()
                os.write(head.toByteArray(Charsets.ISO_8859_1))
                os.write(data)
                os.flush()
            } catch (_: Exception) { }
        }
    }

    private fun readLine(input: InputStream): String? {
        val sb = StringBuilder()
        while (true) {
            val b = input.read()
            if (b < 0) return if (sb.isEmpty()) null else sb.toString()
            if (b == '\n'.code) return sb.toString().trimEnd('\r')
            sb.append(b.toChar())
        }
    }

    /** True when the PC answers through the link (used by the route picker). */
    fun alive(): Boolean = connected && request("GET", "/api/hello", emptyMap(), null)?.optInt("s") == 200
}
