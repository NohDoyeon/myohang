import type { Metadata, Viewport } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import "./globals.css";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "묘항 (Myohang)",
  description: "고양이들이 사는 항구 마을. 방을 꾸미고, 이웃과 만나고, 화분에 캣닢을 나눠 주세요.",
};

// 폰에서 열었을 때 화면이 축소되지 않게. 이게 없으면 버튼이 손가락보다 작아진다.
// `maximumScale` 은 막지 않는다 — 도트를 확대해서 보고 싶을 수 있고, 확대를 막는 건 접근성을 해친다.
export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  themeColor: "#121110",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="ko" className={`${geistSans.variable} ${geistMono.variable}`}>
      <body>{children}</body>
    </html>
  );
}
