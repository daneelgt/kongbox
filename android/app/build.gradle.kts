plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

// Release signing from ~/.gradle/gradle.properties or the environment (KONGBOX_STORE_FILE, KONGBOX_STORE_PASSWORD,
// KONGBOX_KEY_ALIAS, KONGBOX_KEY_PASSWORD). Without them release builds use the debug key so the project builds anywhere.
fun signingValue(name: String): String? = (project.findProperty(name) as String?) ?: System.getenv(name)

android {
    namespace = "dev.kongbox.panel"
    compileSdk = 35

    defaultConfig {
        applicationId = "dev.kongbox.panel"
        minSdk = 21 // Android 5.0: old phones are the whole point
        targetSdk = 35
        versionCode = 5
        versionName = "0.3.2"
    }

    signingConfigs {
        val storeFile = signingValue("KONGBOX_STORE_FILE")
        if (storeFile != null && file(storeFile).exists()) {
            create("release") {
                this.storeFile = file(storeFile)
                storePassword = signingValue("KONGBOX_STORE_PASSWORD")
                keyAlias = signingValue("KONGBOX_KEY_ALIAS")
                keyPassword = signingValue("KONGBOX_KEY_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = signingConfigs.findByName("release") ?: signingConfigs.getByName("debug")
        }
    }

    buildFeatures { buildConfig = true }

    lint {
        // a framework call missing on Android 5 would crash old phones before the panel opens
        fatal += "NewApi"
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
}
