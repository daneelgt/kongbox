package dev.kongbox.panel

import android.annotation.SuppressLint
import android.app.Activity
import android.app.AlertDialog
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.ActivityInfo
import android.graphics.Color
import android.net.Uri
import android.net.http.SslCertificate
import android.net.http.SslError
import android.os.BatteryManager
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.VibrationEffect
import android.os.Vibrator
import android.text.format.DateFormat
import android.view.HapticFeedbackConstants
import android.view.KeyEvent
import android.view.View
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.view.WindowManager
import android.webkit.ConsoleMessage
import android.webkit.CookieManager
import android.webkit.JavascriptInterface
import android.webkit.SslErrorHandler
import android.webkit.WebChromeClient
import android.webkit.WebResourceError
import android.webkit.WebResourceRequest
import android.webkit.WebView
import android.webkit.WebViewClient
import org.json.JSONObject
import java.security.SecureRandom
import kotlin.concurrent.thread

/**
 * Full-screen WebView showing the agent's panel, over USB (adb reverse) or Wi-Fi (pinned HTTPS).
 * When neither answers, a bundled offline screen shows (with manual pairing) and the app retries every 3 seconds.
 */
class MainActivity : Activity() {
    companion object {
        const val OFFLINE = "file:///android_asset/offline.html"
    }

    private lateinit var web: WebView
    private lateinit var link: AgentLink
    private val main = Handler(Looper.getMainLooper())
    private val bridgeKey = ByteArray(16).also { SecureRandom().nextBytes(it) }.joinToString("") { "%02x".format(it) }
    @Volatile private var volumeKeysFlipPages = true
    @Volatile private var offline = false
    @Volatile private var currentUrl = ""
    private var probing = false

    @SuppressLint("SetJavaScriptEnabled", "AddJavascriptInterface")
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        link = AgentLink(this)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        window.decorView.setBackgroundColor(Color.BLACK)
        if (Build.VERSION.SDK_INT >= 28) {
            window.attributes = window.attributes.apply { layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES }
        }

        // debug builds only: lets Chrome DevTools (chrome://inspect over adb) profile the panel on the phone
        if (BuildConfig.DEBUG) WebView.setWebContentsDebuggingEnabled(true)
        web = WebView(this)
        web.setBackgroundColor(Color.BLACK)
        web.isVerticalScrollBarEnabled = false
        web.isHorizontalScrollBarEnabled = false
        web.overScrollMode = View.OVER_SCROLL_NEVER
        with(web.settings) {
            javaScriptEnabled = true
            domStorageEnabled = true
            mediaPlaybackRequiresUserGesture = false
            setSupportZoom(false)
            builtInZoomControls = false
            displayZoomControls = false
            allowFileAccess = true          // only for the bundled offline screen
            allowContentAccess = false
            @Suppress("DEPRECATION")
            allowFileAccessFromFileURLs = false
            @Suppress("DEPRECATION")
            allowUniversalAccessFromFileURLs = false
        }
        CookieManager.getInstance().setAcceptCookie(true)
        web.webChromeClient = object : WebChromeClient() {
            override fun onConsoleMessage(message: ConsoleMessage): Boolean = true // keep logcat quiet
        }
        web.webViewClient = object : WebViewClient() {
            override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean = !allowed(request.url.toString())

            override fun onPageStarted(view: WebView, url: String?, favicon: android.graphics.Bitmap?) { currentUrl = url ?: "" }

            override fun onReceivedError(view: WebView, request: WebResourceRequest, error: WebResourceError) {
                if (request.isForMainFrame) goOffline()
            }

            /** Wi-Fi: accept the agent's self-signed certificate only if it is the one we paired with. */
            override fun onReceivedSslError(view: WebView, handler: SslErrorHandler, error: SslError) {
                val p = link.pc
                val host = Uri.parse(error.url).host
                val fp = certFingerprint(error.certificate)
                if (p != null && host == link.wifiHost && fp != null && fp.equals(p.fingerprint, ignoreCase = true)) handler.proceed()
                else { handler.cancel(); goOffline() }
            }

            override fun onPageFinished(view: WebView, url: String?) {
                if (url != null && !url.startsWith(OFFLINE)) giveKey(view)
                if (url != null && url.startsWith(OFFLINE)) pushOfflineState()
            }
        }
        web.addJavascriptInterface(Bridge(), "Android")
        web.addJavascriptInterface(PairBridge(), "KongPair")
        setContentView(web)
        hideSystemUi()
        // Android 12+ asks before an app may use paired Bluetooth devices
        if (Build.VERSION.SDK_INT >= 31 && checkSelfPermission(android.Manifest.permission.BLUETOOTH_CONNECT) != android.content.pm.PackageManager.PERMISSION_GRANTED)
            requestPermissions(arrayOf(android.Manifest.permission.BLUETOOTH_CONNECT), 7)
        if (!handlePairLink(intent)) connect()
    }

    private fun allowed(url: String): Boolean {
        if (url.startsWith(OFFLINE) || url.startsWith(AgentLink.USB_PANEL) || url.startsWith(BtBridge.PANEL)) return true
        val h = link.wifiHost ?: return false
        return url.startsWith("https://$h:${AgentLink.WIFI_PORT}/panel/")
    }

    /** DER fingerprint of the certificate WebView rejected (API 29 has getX509Certificate; older ones keep it in saveState). */
    private fun certFingerprint(c: SslCertificate?): String? {
        if (c == null) return null
        return try {
            val der = if (Build.VERSION.SDK_INT >= 29) c.x509Certificate?.encoded
            else SslCertificate.saveState(c).getByteArray("x509-certificate")
            der?.let { AgentLink.sha256(it) }
        } catch (_: Exception) { null }
    }

    /** USB first, then the paired PC over Wi-Fi. */
    private fun connect() {
        thread(isDaemon = true, name = "connect") {
            val url = link.resolve()
            main.post {
                if (url == null) { goOffline(); return@post }
                offline = false
                if (link.route == AgentLink.Route.WIFI) {
                    val p = link.pc!!
                    CookieManager.getInstance().setCookie("https://${link.wifiHost}:${p.port}", "kb=${p.token}; Path=/; Secure; HttpOnly; SameSite=Strict")
                    CookieManager.getInstance().flush()
                }
                web.loadUrl(url)
            }
        }
    }

    /** The key reaches the main frame only, so a sandboxed custom-HTML widget cannot drive the bridge. */
    private fun giveKey(view: WebView) {
        view.evaluateJavascript("window.KB && (KB.key='$bridgeKey', KB.onKey && KB.onKey());", null)
    }

    private fun goOffline() {
        if (!offline) {
            offline = true
            web.loadUrl(OFFLINE)
        }
        probe()
    }

    private fun probe() {
        if (probing) return
        probing = true
        thread(isDaemon = true, name = "probe") {
            while (offline) {
                val url = link.resolve()
                if (url != null) {
                    main.post { if (offline) connect() }
                    break
                }
                Thread.sleep(3000)
            }
            probing = false
        }
    }

    private fun pushOfflineState() {
        val p = link.pc
        val state = JSONObject().put("paired", p != null).put("pc", p?.name ?: "").put("hosts", p?.hosts?.joinToString(", ") ?: "")
        web.evaluateJavascript("window.onState && onState($state);", null)
    }

    // ---------- pairing ----------

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handlePairLink(intent)
    }

    /** kongbox://pair?h=192.168.1.10,10.0.0.5&p=8766&fp=<sha256>&c=123456&n=PC-NAME (the QR code in the editor). */
    private fun handlePairLink(intent: Intent?): Boolean {
        val data = intent?.data ?: return false
        if (data.scheme != "kongbox" || data.host != "pair") return false
        val hosts = (data.getQueryParameter("h") ?: "").split(',').map { it.trim() }.filter { it.isNotEmpty() }
        val port = data.getQueryParameter("p")?.toIntOrNull() ?: AgentLink.WIFI_PORT
        val fp = data.getQueryParameter("fp") ?: ""
        val code = data.getQueryParameter("c") ?: ""
        val name = data.getQueryParameter("n") ?: "PC"
        if (hosts.isEmpty() || fp.length != 64 || code.length != 6) { connect(); return true }
        AlertDialog.Builder(this)
            .setTitle("Parear com $name?")
            .setMessage("Código de verificação: ${AgentLink.checkCode(fp)}\nConfira se é o mesmo que aparece no PC.")
            .setPositiveButton("Parear") { _, _ -> pairWith(hosts, port, fp, code) }
            .setNegativeButton("Cancelar") { _, _ -> connect() }
            .setCancelable(false)
            .show()
        return true
    }

    private fun pairWith(hosts: List<String>, port: Int, fp: String, code: String, done: ((String?) -> Unit)? = null) {
        thread(isDaemon = true, name = "pair") {
            var error: String? = "Nenhum endereço do PC respondeu."
            for (h in hosts) {
                val (pc, err) = link.claim(h, port, fp, code, Build.MODEL ?: "Android")
                if (pc != null) {
                    // keep every address from the QR code, the one that answered first
                    link.pc = pc.copy(hosts = listOf(h) + hosts.filter { it != h })
                    error = null
                    break
                }
                error = err
            }
            main.post {
                if (done != null) done(error)
                else if (error != null) AlertDialog.Builder(this).setTitle("Pareamento falhou").setMessage(error).setPositiveButton("OK", null).show()
                if (error == null) { offline = false; connect() }
            }
        }
    }

    // ---------- system UI & keys ----------

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) hideSystemUi()
    }

    @Suppress("DEPRECATION")
    private fun hideSystemUi() {
        if (Build.VERSION.SDK_INT >= 30) {
            window.setDecorFitsSystemWindows(false)
            window.insetsController?.let {
                it.hide(WindowInsets.Type.systemBars())
                it.systemBarsBehavior = WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            }
        } else {
            window.decorView.systemUiVisibility = (View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY or View.SYSTEM_UI_FLAG_FULLSCREEN
                or View.SYSTEM_UI_FLAG_HIDE_NAVIGATION or View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                or View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION or View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN)
        }
    }

    override fun dispatchKeyEvent(event: KeyEvent): Boolean {
        if (volumeKeysFlipPages && !offline && (event.keyCode == KeyEvent.KEYCODE_VOLUME_UP || event.keyCode == KeyEvent.KEYCODE_VOLUME_DOWN)) {
            if (event.action == KeyEvent.ACTION_DOWN && event.repeatCount == 0) {
                val dir = if (event.keyCode == KeyEvent.KEYCODE_VOLUME_UP) -1 else 1
                web.evaluateJavascript("window.KB && KB.flip && KB.flip($dir);", null)
            }
            return true
        }
        return super.dispatchKeyEvent(event)
    }

    @Deprecated("Kiosk: back does nothing")
    override fun onBackPressed() { /* the panel is the whole app */ }

    override fun onResume() {
        super.onResume()
        web.onResume()
        hideSystemUi()
    }

    override fun onPause() {
        web.onPause()
        super.onPause()
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode == 7 && offline) probe()
    }

    override fun onDestroy() {
        offline = false
        web.destroy()
        super.onDestroy()
    }

    /** window.Android — panel only; every call carries the key handed to the main frame. */
    inner class Bridge {
        private fun ok(key: String) = key == bridgeKey

        @JavascriptInterface
        fun haptic(key: String) {
            if (ok(key)) main.post { web.performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP) }
        }

        @JavascriptInterface
        fun buzz(key: String, pattern: String) {
            if (!ok(key)) return
            val p = pattern.split(',').mapNotNull { it.trim().toLongOrNull() }.take(12).map { it.coerceIn(0, 1500) }.toLongArray()
            if (p.isEmpty()) return
            @Suppress("DEPRECATION")
            val v = getSystemService(Context.VIBRATOR_SERVICE) as? Vibrator ?: return
            if (Build.VERSION.SDK_INT >= 26) v.vibrate(VibrationEffect.createWaveform(p, -1))
            else @Suppress("DEPRECATION") v.vibrate(p, -1)
        }

        /** 0…1, or -1 to hand brightness back to the system. */
        @JavascriptInterface
        fun setBrightness(key: String, value: Float) {
            if (!ok(key)) return
            main.post {
                window.attributes = window.attributes.apply {
                    screenBrightness = if (value < 0) WindowManager.LayoutParams.BRIGHTNESS_OVERRIDE_NONE else value.coerceIn(0.01f, 1f)
                }
            }
        }

        @JavascriptInterface
        fun battery(key: String): String {
            if (!ok(key)) return "{}"
            val i: Intent = registerReceiver(null, IntentFilter(Intent.ACTION_BATTERY_CHANGED)) ?: return "{}"
            val level = i.getIntExtra(BatteryManager.EXTRA_LEVEL, -1)
            val scale = i.getIntExtra(BatteryManager.EXTRA_SCALE, 100).coerceAtLeast(1)
            val status = i.getIntExtra(BatteryManager.EXTRA_STATUS, -1)
            val temp = i.getIntExtra(BatteryManager.EXTRA_TEMPERATURE, 0) / 10.0
            return JSONObject()
                .put("level", if (level < 0) JSONObject.NULL else level * 100 / scale)
                .put("charging", status == BatteryManager.BATTERY_STATUS_CHARGING || status == BatteryManager.BATTERY_STATUS_FULL)
                .put("temp", temp)
                .toString()
        }

        @JavascriptInterface
        fun setVolumeKeys(key: String, enabled: Boolean) { if (ok(key)) volumeKeysFlipPages = enabled }

        @JavascriptInterface
        fun is24Hour(key: String): Boolean = if (ok(key)) DateFormat.is24HourFormat(this@MainActivity) else true

        @JavascriptInterface
        fun setOrientation(key: String, mode: String) {
            if (!ok(key)) return
            main.post {
                requestedOrientation = when (mode) {
                    "landscape" -> ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
                    "portrait" -> ActivityInfo.SCREEN_ORIENTATION_SENSOR_PORTRAIT
                    else -> ActivityInfo.SCREEN_ORIENTATION_FULL_USER
                }
            }
        }

        /** "usb" or "wifi", for the status bar. */
        @JavascriptInterface
        fun transport(key: String): String = if (ok(key)) link.route.name.lowercase() else ""

        /** The panel calls this when polls start failing: find a working route (USB ↔ Wi-Fi) and switch to it. */
        @JavascriptInterface
        fun reconnect(key: String) { if (ok(key)) main.post { connect() } }

        @JavascriptInterface
        fun version(key: String): String = if (ok(key)) BuildConfig.VERSION_NAME else ""
    }

    /** window.KongPair — only answers while the bundled offline screen is showing (manual pairing). */
    inner class PairBridge {
        private fun onOffline() = currentUrl.startsWith(OFFLINE)

        /** Step 1: reach the PC, record its certificate and report the check code to compare on both screens. */
        @JavascriptInterface
        fun probe(host: String) {
            if (!onOffline()) return
            val h = host.trim()
            thread(isDaemon = true) {
                val (fp, name) = link.probeFingerprint(h, AgentLink.WIFI_PORT)
                val res = if (fp == null) JSONObject().put("ok", false)
                else JSONObject().put("ok", true).put("check", AgentLink.checkCode(fp)).put("fp", fp).put("name", name ?: h)
                main.post { web.evaluateJavascript("window.onProbe && onProbe($res);", null) }
            }
        }

        /** Step 2: the user confirmed the check code; claim with the code shown in the editor. */
        @JavascriptInterface
        fun claim(host: String, fp: String, code: String) {
            if (!onOffline() || fp.length != 64) return
            main.post {
                pairWith(listOf(host.trim()), AgentLink.WIFI_PORT, fp, code.trim()) { err ->
                    val res = JSONObject().put("ok", err == null).put("error", err ?: "")
                    web.evaluateJavascript("window.onClaim && onClaim($res);", null)
                }
            }
        }

        @JavascriptInterface
        fun forget() { if (onOffline()) { link.pc = null; main.post { pushOfflineState() } } }
    }
}
