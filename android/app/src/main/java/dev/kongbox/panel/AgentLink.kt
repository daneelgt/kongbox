package dev.kongbox.panel

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.SSLContext
import javax.net.ssl.X509TrustManager

/**
 * How the phone reaches the agent: USB first (adb reverse → http://127.0.0.1:8765), then the paired PC over Wi-Fi
 * (https://<ip>:8766, certificate pinned by SHA-256, token in an HttpOnly cookie).
 */
class AgentLink(context: Context) {
    enum class Route { USB, WIFI, NONE }

    data class Pc(val name: String, val hosts: List<String>, val port: Int, val fingerprint: String, val token: String)

    companion object {
        const val USB_PANEL = "http://127.0.0.1:8765/panel/"
        const val USB_PROBE = "http://127.0.0.1:8765/api/info"
        const val WIFI_PORT = 8766

        fun sha256(bytes: ByteArray): String =
            MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02x".format(it) }

        fun checkCode(fingerprint: String): String =
            if (fingerprint.length < 8) "" else (fingerprint.substring(0, 4) + " " + fingerprint.substring(4, 8)).uppercase()
    }

    private val prefs = context.getSharedPreferences("kongbox", Context.MODE_PRIVATE)

    @Volatile var route = Route.NONE
        private set
    @Volatile var wifiHost: String? = null
        private set

    var pc: Pc?
        get() {
            val raw = prefs.getString("pc", null) ?: return null
            return try {
                val o = JSONObject(raw)
                val hosts = o.getJSONArray("hosts").let { a -> (0 until a.length()).map { a.getString(it) } }
                Pc(o.getString("name"), hosts, o.optInt("port", WIFI_PORT), o.getString("fp"), o.getString("token"))
            } catch (_: Exception) { null }
        }
        set(value) {
            if (value == null) { prefs.edit().remove("pc").apply(); return }
            prefs.edit().putString("pc", JSONObject()
                .put("name", value.name).put("hosts", JSONArray(value.hosts)).put("port", value.port)
                .put("fp", value.fingerprint).put("token", value.token).toString()).apply()
        }

    /** Trust manager that accepts exactly one certificate (or, while pairing by hand, records whatever it sees). */
    class PinTrust(private val expected: String?) : X509TrustManager {
        @Volatile var seen: String? = null
        override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {
            val f = sha256(chain[0].encoded)
            seen = f
            if (expected != null && !f.equals(expected, ignoreCase = true)) throw CertificateException("certificate does not match the paired PC")
        }
        override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) = throw CertificateException("not a server")
        override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
    }

    /** One HTTPS request to the agent with the certificate pinned (or recorded when [pin] is null). */
    fun https(url: String, pin: String?, method: String = "GET", body: String? = null, timeoutMs: Int = 2500): Triple<Int, String, String?> {
        val trust = PinTrust(pin)
        val ctx = SSLContext.getInstance("TLS").apply { init(null, arrayOf(trust), null) }
        val c = URL(url).openConnection() as HttpsURLConnection
        c.sslSocketFactory = ctx.socketFactory
        c.hostnameVerifier = javax.net.ssl.HostnameVerifier { _, _ -> true } // the pin replaces the hostname check
        c.connectTimeout = timeoutMs
        c.readTimeout = timeoutMs
        c.requestMethod = method
        if (body != null) {
            c.doOutput = true
            c.setRequestProperty("Content-Type", "application/json")
            c.outputStream.use { it.write(body.toByteArray()) }
        }
        return try {
            val code = c.responseCode
            val text = (if (code < 400) c.inputStream else c.errorStream)?.bufferedReader()?.readText() ?: ""
            Triple(code, text, trust.seen)
        } finally { c.disconnect() }
    }

    private fun usbAlive(): Boolean = try {
        val c = URL(USB_PROBE).openConnection() as HttpURLConnection
        c.connectTimeout = 1200
        c.readTimeout = 1200
        val ok = c.responseCode == 200
        c.disconnect()
        ok
    } catch (_: Exception) { false }

    /** Blocking: finds a working route and returns the panel URL to load, or null. Call off the main thread. */
    fun resolve(): String? {
        if (usbAlive()) { route = Route.USB; wifiHost = null; return USB_PANEL }
        val p = pc ?: run { route = Route.NONE; return null }
        for (h in p.hosts) {
            try {
                val (code, _, _) = https("https://$h:${p.port}/api/hello", p.fingerprint, timeoutMs = 2000)
                if (code == 200) { route = Route.WIFI; wifiHost = h; return "https://$h:${p.port}/panel/" }
            } catch (_: Exception) { /* next address */ }
        }
        route = Route.NONE
        return null
    }

    /** Exchanges a pairing code for a token. [pin] is the fingerprint from the QR code, or the one the user confirmed. */
    fun claim(host: String, port: Int, pin: String, code: String, phoneName: String): Pair<Pc?, String?> = try {
        val (status, text, seen) = https("https://$host:$port/api/pair/claim", pin, "POST",
            JSONObject().put("code", code).put("name", phoneName).toString(), 5000)
        val o = JSONObject(text.ifEmpty { "{}" })
        if (status == 200 && o.has("token")) {
            val p = Pc(o.optString("host", host), listOf(host), port, seen ?: pin, o.getString("token"))
            pc = p
            Pair(p, null)
        } else Pair(null, o.optString("error", "Falha no pareamento ($status)."))
    } catch (e: Exception) { Pair(null, "Não consegui falar com o PC: ${e.message}") }

    /** Manual pairing step 1: reach the host and record its fingerprint so the user can compare the check code. */
    fun probeFingerprint(host: String, port: Int): Pair<String?, String?> = try {
        val (code, text, seen) = https("https://$host:$port/api/hello", null)
        if (code == 200 && seen != null) Pair(seen, JSONObject(text).optString("host", host)) else Pair(null, null)
    } catch (_: Exception) { Pair(null, null) }
}
