# 数学公式

公式由 RaTeX（KaTeX 级覆盖，含 mhchem）排版，行内用 `$…$` 或 `\(…\)`，块级用 `$$…$$` 或 `\[…\]`。

## 行内公式

质能方程 $E = mc^2$，欧拉恒等式 $e^{i\pi} + 1 = 0$，求和 $\sum_{k=1}^{n} k = \frac{n(n+1)}{2}$，以及 \(\alpha + \beta = \gamma\)。

## 块级公式

$$
\int_{-\infty}^{\infty} e^{-x^2}\,dx = \sqrt{\pi}
$$

$$
f(x) = \sum_{n=0}^{\infty} \frac{f^{(n)}(a)}{n!}(x-a)^n
$$

## 多行对齐

\[
\begin{aligned}
\nabla \cdot \mathbf{E} &= \frac{\rho}{\varepsilon_0} \\
\nabla \cdot \mathbf{B} &= 0 \\
\nabla \times \mathbf{E} &= -\frac{\partial \mathbf{B}}{\partial t} \\
\nabla \times \mathbf{B} &= \mu_0 \mathbf{J} + \mu_0 \varepsilon_0 \frac{\partial \mathbf{E}}{\partial t}
\end{aligned}
\]

## 矩阵与分段函数

$$
A = \begin{pmatrix} a_{11} & a_{12} & \cdots & a_{1n} \\ a_{21} & a_{22} & \cdots & a_{2n} \\ \vdots & \vdots & \ddots & \vdots \\ a_{m1} & a_{m2} & \cdots & a_{mn} \end{pmatrix}
\qquad
|x| = \begin{cases} x, & x \ge 0 \\ -x, & x < 0 \end{cases}
$$

## 化学方程式

$$
\ce{CO2 + C -> 2 CO}
\qquad
\ce{Hg^2+ ->[I-] HgI2 ->[I-] [Hg^{II}I4]^2-}
$$

行内：$\ce{2H2 + O2 -> 2H2O}$，$\ce{SO4^2- + Ba^2+ -> BaSO4 v}$。

## 错误的公式

写错的公式会显示错误信息而不是让整篇渲染失败：$\frac{1}{$。
